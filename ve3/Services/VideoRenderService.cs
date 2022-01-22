using FFmpeg.AutoGen;

namespace ve3.Services;

unsafe class VideoRenderService
{
    readonly int outputWidth, outputHeight;

    readonly AVFormatContext* formatContext;
    readonly AVStream* videoStream;
    readonly AVCodecContext* codecDecoderContext;
    readonly AVFrame* inputFrame = ffmpeg.av_frame_alloc();
    readonly AVPacket* inputPacket = ffmpeg.av_packet_alloc();

    readonly SwsContext* toRgbSwsContext;

    TimeSpan? seekTimeStamp;

    const int framesMaxCount = 30;
    readonly Queue<PointerWrapper<AVFrame>> frames = new();
    readonly object framesQueueSync = new();
    readonly Thread decodingThread;

    readonly int rgbFrameDataLength;
    readonly byte*[] rgbFrameData;
    readonly int[] rgbFrameStride;

    void CheckAvSuccess(int ret, int allowedResult)
    {
        if (ret != allowedResult)
            CheckAvSuccess(ret);
    }

    void CheckAvSuccess(int ret)
    {
        if (ret < 0)
        {
            const int bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(ret, buffer, bufferSize);

            throw new VideoException(Marshal.PtrToStringUTF8(new(buffer)));
        }
    }

    public VideoRenderService(string path, Func<int, int, (int width, int height)> resizeFunc)
    {
        fixed (AVFormatContext** pFormatContext = &formatContext)
            CheckAvSuccess(ffmpeg.avformat_open_input(pFormatContext, path, null, null));
        CheckAvSuccess(ffmpeg.avformat_find_stream_info(formatContext, null));

        for (var pStream = *formatContext->streams; pStream < formatContext->streams[formatContext->nb_streams]; ++pStream)
            if (pStream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                videoStream = pStream;
                break;
            }
        if (videoStream is null)
            throw new VideoException("Could not find a video stream");

        // decoder
        var codecDecoder = ffmpeg.avcodec_find_decoder(videoStream->codecpar->codec_id);
        if (codecDecoder is null)
            throw new VideoException("Could not find decoder codec");

        // copy the decoder codec context locally, since we must not use the the global version
        codecDecoderContext = ffmpeg.avcodec_alloc_context3(codecDecoder);
        CheckAvSuccess(ffmpeg.avcodec_parameters_to_context(codecDecoderContext, videoStream->codecpar));

        // multi-threaded decoder, use 4 threads
        codecDecoderContext->thread_count = 4;
        codecDecoderContext->thread_type = ffmpeg.FF_THREAD_FRAME;

        // scaler context
        (OutputWidth, OutputHeight) = resizeFunc(videoStream->codecpar->width, videoStream->codecpar->height);
        rgbFrameData = new[] { (byte*)NativeMemory.Alloc((nuint)(rgbFrameDataLength = OutputWidth * OutputHeight * 3)) };
        rgbFrameStride = new[] { OutputWidth * 3 };
        toRgbSwsContext = ffmpeg.sws_getContext(videoStream->codecpar->width, videoStream->codecpar->height,
            codecDecoderContext->pix_fmt, OutputWidth, OutputHeight, AVPixelFormat.AV_PIX_FMT_RGB24, ffmpeg.SWS_BILINEAR, null, null, null);

        // open the codec
        CheckAvSuccess(ffmpeg.avcodec_open2(codecDecoderContext, codecDecoder, null));

        decodingThread = new Thread(() =>
        {
            while (true)
            {
                TimeSpan? seekTimeStamp;
                long tsPts = long.MinValue;
                lock (framesQueueSync)
                {
                    seekTimeStamp = this.seekTimeStamp;
                    this.seekTimeStamp = default;
                }

                if (seekTimeStamp.HasValue)
                {
                    // convert seconds to pts
                    tsPts = (long)(seekTimeStamp.Value.TotalSeconds / ffmpeg.av_q2d(videoStream->time_base));

                    // seek before the time stamp
                    CheckAvSuccess(ffmpeg.avformat_seek_file(formatContext, videoStream->index, long.MinValue, tsPts, tsPts, ffmpeg.AVSEEK_FLAG_BACKWARD));

                    // flush the context
                    ffmpeg.avcodec_flush_buffers(codecDecoderContext);
                }

                ReadNextFrame(tsPts, frame =>
                {
                    var newFrame = DeepCloneFrame(frame.Data);

                    lock (framesQueueSync)
                    {
                        while (true)
                        {
                            if (this.seekTimeStamp.HasValue || frames.Count < framesMaxCount)
                                break;
                            Monitor.Wait(framesQueueSync);
                        }

                        // seek if required
                        if (this.seekTimeStamp.HasValue)
                        {
                            ffmpeg.av_frame_unref(newFrame);
                            return;
                        }

                        // otherwise queue the frame
                        frames.Enqueue(new(newFrame));
                    }
                });
            }
        })
        { Name = $"Video Decoding Thread ({path})", IsBackground = true };
        decodingThread.Start();
    }

    public delegate void ConsumeFrameDelegate(TimeSpan ts, TimeSpan duration, Span<byte> rgbData, int stride, int queuedFrameCount);
    public bool ConsumeFrame(ConsumeFrameDelegate process)
    {
        lock (framesQueueSync)
        {
            if (frames.Count == 0)
                return false;

            var frame = frames.Dequeue();

            CheckAvSuccess(ffmpeg.sws_scale(toRgbSwsContext, frame.Data->data, frame.Data->linesize, 0, videoStream->codecpar->height, rgbFrameData, rgbFrameStride));

            process(TimeSpan.FromSeconds(frame.Data->best_effort_timestamp * TimeBase),
                TimeSpan.FromSeconds(frame.Data->pkt_duration * TimeBase),
                new(rgbFrameData[0], rgbFrameDataLength), 3 * OutputWidth, frames.Count - 1);

            ForceRedisplay = false;

            ffmpeg.av_frame_free(&frame.Data);

            Monitor.Pulse(framesQueueSync);
        }

        return true;
    }

    public void SeekPts(TimeSpan ts)
    {
        seekTimeStamp = ts;

        lock (framesQueueSync)
            ClearFramesQueue();
        ForceRedisplay = true;
    }

    /// <summary>
    /// Clears the frames queue. Needs to be under a <c>lock(framesQueueSync)</c>.
    /// </summary>
    void ClearFramesQueue()
    {
        while (frames.Count > 0)
            ffmpeg.av_frame_unref(frames.Dequeue().Data);
        Monitor.PulseAll(framesQueueSync);
    }

    public double TimeBase => ffmpeg.av_q2d(videoStream->time_base);
    public TimeSpan Start => TimeSpan.FromSeconds(videoStream->start_time * TimeBase);
    public TimeSpan Duration => TimeSpan.FromSeconds(formatContext->duration / ffmpeg.AV_TIME_BASE);

    public int OutputWidth { get; private init; }
    public int OutputHeight { get; private init; }

    public bool ForceRedisplay { get; set; } = true;

    struct PointerWrapper<TPointer> where TPointer : unmanaged
    {
        public readonly TPointer* Data;

        public PointerWrapper(TPointer* data) => Data = data;
    }

    enum ProcessedFrameResult
    {
        Processed,
        Skipped,
        Eof,
        None
    }
    int ReadNextFrame(long skipPts, Action<PointerWrapper<AVFrame>> processFrame)
    {
        ProcessedFrameResult processFrames()
        {
            var res = ffmpeg.avcodec_receive_frame(codecDecoderContext, inputFrame);
            if (res == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                return ProcessedFrameResult.None;
            if (res == ffmpeg.AVERROR_EOF)
                return ProcessedFrameResult.Eof;
            CheckAvSuccess(res);
            inputFrame->pts = inputFrame->best_effort_timestamp;

            // skip frames as needed for seeking
            if (inputFrame->pts >= skipPts)
                processFrame(new(inputFrame));
            else
                return ProcessedFrameResult.Skipped;

            ffmpeg.av_frame_unref(inputFrame);

            return 0;
        }

        // process frames until we have nothing left in the buffer
        while (true)
            switch (processFrames())
            {
                case ProcessedFrameResult.Processed:
                    return 0;
                case ProcessedFrameResult.None:
                    goto readNextFrame;
                case ProcessedFrameResult.Eof:
                    return ffmpeg.AVERROR_EOF;
            }

        readNextFrame:
        while (ffmpeg.av_read_frame(formatContext, inputPacket) >= 0)
        {
            if (inputPacket->stream_index == videoStream->index)
            {
                CheckAvSuccess(ffmpeg.avcodec_send_packet(codecDecoderContext, inputPacket));

                while (true)
                    switch (processFrames())
                    {
                        case ProcessedFrameResult.Processed:
                            return 0;
                        case ProcessedFrameResult.None:
                            goto readNextFrame;
                        case ProcessedFrameResult.Eof:
                            return ffmpeg.AVERROR_EOF;
                    }
            }

            ffmpeg.av_packet_unref(inputPacket);
        }

        return ffmpeg.AVERROR_EOF;
    }

    AVFrame* DeepCloneFrame(AVFrame* src)
    {
        var dst = ffmpeg.av_frame_alloc();
        dst->format = src->format;
        dst->width = src->width;
        dst->height = src->height;
        dst->channels = src->channels;
        dst->channel_layout = src->channel_layout;
        dst->nb_samples = src->nb_samples;

        CheckAvSuccess(ffmpeg.av_frame_get_buffer(dst, 32));
        CheckAvSuccess(ffmpeg.av_frame_copy(dst, src));
        CheckAvSuccess(ffmpeg.av_frame_copy_props(dst, src));

        return dst;
    }
}

public class VideoException : Exception
{
    public VideoException(string? message) : base(message)
    {
    }

    public VideoException(string? message, Exception innerException) : base(message, innerException)
    {
    }

    public VideoException()
    {
    }
}