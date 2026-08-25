using System.Windows;

namespace TensileNeW;

public partial class VisionDetectionSettingsWindow : Window
{
    public VisionDetectionSettingsWindow() => InitializeComponent();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
