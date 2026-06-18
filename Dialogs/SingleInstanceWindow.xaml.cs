using System.Windows;

namespace TensileNeW;

public partial class SingleInstanceWindow : Window
{
    public SingleInstanceWindow()
    {
        InitializeComponent();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
