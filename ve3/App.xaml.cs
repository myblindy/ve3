namespace ve3;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        TinyIoCContainer.Current.Register<IDialogService>((_, _) => new DialogService());
    }
}
