using System.Diagnostics;

namespace ve3.Views;

public partial class MainView : Window
{
    readonly VideoRenderService videoRenderService;
    readonly WriteableBitmap videoFrameWriteableBitmap;
    readonly object videoUpdateSync = new();

    public static readonly DependencyProperty QueuedFramesCountProperty =
        DependencyProperty.Register("QueuedFramesCount", typeof(int), typeof(MainView), new PropertyMetadata(0));

    public int QueuedFramesCount
    {
        get { return (int)GetValue(QueuedFramesCountProperty); }
        set { SetValue(QueuedFramesCountProperty, value); }
    }

    public static readonly DependencyProperty VideoDurationProperty =
        DependencyProperty.Register("VideoDuration", typeof(double), typeof(MainView), new PropertyMetadata(0.0));

    public double VideoDuration
    {
        get { return (double)GetValue(VideoDurationProperty); }
        set { SetValue(VideoDurationProperty, value); }
    }

    public static readonly DependencyProperty VideoPositionProperty =
        DependencyProperty.Register("VideoPosition", typeof(double), typeof(MainView), new PropertyMetadata(0.0));

    public double VideoPosition
    {
        get { return (double)GetValue(VideoPositionProperty); }
        set { SetValue(VideoPositionProperty, value); }
    }

    public MainView()
    {
        InitializeComponent();

        videoRenderService = new(@"E:\vids\sexy\190517 TWICE - FANCY sana fancam bouncy boobs thighs cute face blonde.mkv", (w, h) => (720, 1280));
        VideoDuration = videoRenderService.Duration.TotalSeconds;
        VideoFrameImage.Source = videoFrameWriteableBitmap =
            new WriteableBitmap(videoRenderService.OutputWidth, videoRenderService.OutputHeight, 96, 96, PixelFormats.Rgb24, null);

        var lastFrameRgbData = new byte[videoRenderService.OutputWidth * videoRenderService.OutputHeight * 3];
        int lastFrameStride = 0;
        var newFrame = false;

        CompositionTarget.Rendering += (s, e) =>
        {
            lock (videoUpdateSync)
                if (newFrame)
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

            while (true)
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
                while (sw.Elapsed < cummulativeDuration)
                {
                    if (cummulativeDuration - sw.Elapsed > TimeSpan.FromMilliseconds(10))
                        Thread.Sleep(TimeSpan.FromMilliseconds(5));
                }
            }
        })
        { Name = "Render Thread", IsBackground = true }.Start();
    }
}
