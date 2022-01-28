using MvvmDialogs.FrameworkDialogs.OpenFile;

namespace ve3.ViewModels;

class MainViewModel : ReactiveObject
{
    public MainViewModel(IDialogService dialogService)
    {
        var filter = string.Join(';', VideoRenderService.SupportedFormats.Select(fmt => $"*.{fmt}"));
        OpenVideoCommand = ReactiveCommand.Create(() =>
        {
            OpenFileDialogSettings settings = new()
            {
                Filter = $"Video Files ({filter})|{filter}|All Files|*",
            };
            if (dialogService.ShowOpenFileDialog(this, settings) == true)
                VideoModel.FileName = settings.FileName;
        });
    }

    public VideoModel VideoModel { get; } = new();
    public ObservableCollection<VideoClipModel> VideoClips { get; } = new();

    public ICommand OpenVideoCommand { get; }
}
