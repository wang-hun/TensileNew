using System.Windows;
using TensileNeW.Models;

namespace TensileNeW;

public partial class VisionDetectionSettingsWindow : Window
{
    public VisionDetectionSettingsWindow() => InitializeComponent();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UseVisionDetection_Changed(object sender, RoutedEventArgs e)
    {
        RAM.SaveSettingModel();
    }
}
