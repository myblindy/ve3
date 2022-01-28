namespace ve3.ViewModels;

class ViewModelLocator
{
    public ViewModelLocator() =>
        SimpleIoc.Default.Register<MainViewModel>();

    public MainViewModel MainViewModel => SimpleIoc.Default.GetInstance<MainViewModel>();
}
