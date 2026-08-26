using System.Windows;
using TensileNeW.Models;
using TensileNeW.Services;

namespace TensileNeW;

public partial class VisionDetectionSettingsWindow : Window
{
    private readonly VisionDeviceClient _visionDeviceClient;
    public VisionDetectionSettingsWindow(VisionDeviceClient visionDeviceClient)
    {
        _visionDeviceClient = visionDeviceClient;
        InitializeComponent();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UseVisionDetection_Changed(object sender, RoutedEventArgs e)
    {
        RAM.SaveSettingModel();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingModel setting || !setting.VisionModuleEnabled) return;
        ConnectionStatusText.Text = "连接中...";
        bool connected = await _visionDeviceClient.ConnectAsync(setting.VisionDeviceIp, setting.VisionDevicePort, TimeSpan.FromSeconds(5));
        ConnectionStatusText.Text = connected ? "已连接" : "连接失败";
        if (connected) RAM.SaveSettingModel();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }
}
