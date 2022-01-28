namespace ve3;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        SimpleIoc.Default.Register<IDialogService>(() => new DialogService());
    }
}
