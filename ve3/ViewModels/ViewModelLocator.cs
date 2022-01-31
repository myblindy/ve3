namespace ve3.ViewModels;

class ViewModelLocator
{
    public ViewModelLocator() =>
        TinyIoCContainer.Current.Register<MainViewModel>();

    public MainViewModel MainViewModel => TinyIoCContainer.Current.Resolve<MainViewModel>();
}
