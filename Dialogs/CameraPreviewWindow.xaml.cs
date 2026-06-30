using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace TensileNeW;

public partial class CameraPreviewWindow : Window
{
    public CameraPreviewWindow()
    {
        InitializeComponent();
    }

    public void SetPreviewSource(BitmapSource? source)
    {
        PreviewImage.Source = source;
        EmptyTextBlock.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
