using System.Windows.Controls.Primitives;

namespace ve3.Controls;

class ToolbarButtonExtra
{
    public static ImageSource GetImageSource(DependencyObject obj) => (ImageSource)obj.GetValue(ImageSourceProperty);
    public static void SetImageSource(DependencyObject obj, ImageSource value) => obj.SetValue(ImageSourceProperty, value);

    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.RegisterAttached("ImageSource", typeof(ImageSource), typeof(ToolbarButtonExtra));
}
