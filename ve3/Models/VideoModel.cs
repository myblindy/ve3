namespace ve3.Models;

class VideoModel : ReactiveObject
{
    string? fileName;
    public string? FileName { get => fileName; set => this.RaiseAndSetIfChanged(ref fileName, value); }
}
