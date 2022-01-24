namespace ve3.Controls;

public partial class VideoControl : UserControl, INotifyPropertyChanged
{
    VideoRenderService? videoRenderService;
    WriteableBitmap? videoFrameWriteableBitmap;
    readonly object videoUpdateSync = new();

    public static readonly DependencyProperty QueuedFramesCountProperty =
        DependencyProperty.Register("QueuedFramesCount", typeof(int), typeof(VideoControl), new PropertyMetadata(0));

    public int QueuedFramesCount
    {
        get { return (int)GetValue(QueuedFramesCountProperty); }
        set { SetValue(QueuedFramesCountProperty, value); }
    }

    public static readonly DependencyProperty VideoDurationProperty =
        DependencyProperty.Register("VideoDuration", typeof(double), typeof(VideoControl), new PropertyMetadata(0.0));

    public double VideoDuration
    {
        get { return (double)GetValue(VideoDurationProperty); }
        set { SetValue(VideoDurationProperty, value); }
    }

    public static readonly DependencyProperty VideoPositionProperty =
        DependencyProperty.Register("VideoPosition", typeof(double), typeof(VideoControl), new PropertyMetadata(0.0));

    public double VideoPosition
    {
        get { return (double)GetValue(VideoPositionProperty); }
        set { SetValue(VideoPositionProperty, value); }
    }

    public static readonly DependencyProperty FileNameProperty =
        DependencyProperty.Register("FileName", typeof(string), typeof(VideoControl), new PropertyMetadata(FileNameChanged));

    private static void FileNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctl = (VideoControl)d;
        ctl.LoadFile((string)e.NewValue);
    }

    public string FileName
    {
        get { return (string)GetValue(FileNameProperty); }
        set { SetValue(FileNameProperty, value); }
    }

    bool isPlaying;
    public bool IsPlaying { get => isPlaying; set { if (isPlaying != value) { isPlaying = value; PropertyChanged?.Invoke(this, new(nameof(IsPlaying))); } } }

    bool isNewFile;
    byte[]? lastFrameRgbData;
    int lastFrameStride;
    bool newFrame;

    public event PropertyChangedEventHandler? PropertyChanged;

    void LoadFile(string path)
    {
        videoRenderService?.Dispose();
        videoRenderService = new(path, (w, h) => (1280, 720));
        VideoDuration = videoRenderService.Duration.TotalSeconds;
        VideoFrameImage.Source = videoFrameWriteableBitmap =
            new WriteableBitmap(videoRenderService.OutputWidth, videoRenderService.OutputHeight, 96, 96, PixelFormats.Rgb24, null);

        lastFrameRgbData = new byte[videoRenderService.OutputWidth * videoRenderService.OutputHeight * 3];

        isNewFile = true;
    }

    public VideoControl()
    {
        InitializeComponent();

        CompositionTarget.Rendering += (s, e) =>
        {
            if (videoRenderService is not null)
                lock (videoUpdateSync)
                    if (videoFrameWriteableBitmap is not null && newFrame)
                    {
                        videoFrameWriteableBitmap.Lock();
                        videoFrameWriteableBitmap.WritePixels(new(0, 0, videoRenderService.OutputWidth, videoRenderService.OutputHeight),
                            lastFrameRgbData, lastFrameStride, 0);
                        videoFrameWriteableBitmap.Unlock();

                        newFrame = false;
                    }
        };

        new Thread(_ =>
        {
            var sw = Stopwatch.StartNew();
            var cummulativeDuration = TimeSpan.Zero;
            var totalTimeSpentPaused = TimeSpan.Zero;
            var lastElapsed = TimeSpan.Zero;

            while (true)
            {
                var elapsed = sw.Elapsed;
                var delta = elapsed - lastElapsed;
                lastElapsed = elapsed;

                if (isNewFile)
                {
                    sw = Stopwatch.StartNew();
                    totalTimeSpentPaused = cummulativeDuration = TimeSpan.Zero;
                    isNewFile = false;
                }

                if (!IsPlaying || videoRenderService is null)
                {
                    Thread.Sleep(1);
                    if (videoRenderService is not null)
                        totalTimeSpentPaused += delta;
                }
                else
                {
                    while (!videoRenderService.ConsumeFrame((ts, duration, rgbData, stride, queuedFramesCount) =>
                    {
                        lock (videoUpdateSync)
                            unsafe
                            {
                                lastFrameStride = stride;
                                newFrame = true;

                                rgbData.CopyTo(lastFrameRgbData);
                            }
                        cummulativeDuration += duration;

                        Dispatcher.BeginInvoke(() =>
                        {
                            QueuedFramesCount = queuedFramesCount;
                            VideoPosition = ts.TotalSeconds;
                        });
                    }))
                    {
                        // frame queue is empty, request again
                    }

                    // wait for the correct amount of time to elapse
                    while (sw.Elapsed - totalTimeSpentPaused < cummulativeDuration)
                    {
                        if (cummulativeDuration - sw.Elapsed + totalTimeSpentPaused > TimeSpan.FromMilliseconds(5))
                            Thread.Sleep(TimeSpan.FromMilliseconds(3));
                    }
                }
            }
        })
        { Name = "Render Thread", IsBackground = true }.Start();
    }
}
