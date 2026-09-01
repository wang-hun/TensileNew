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
        _visionDeviceClient.ConnectionClosed += VisionDeviceClient_ConnectionClosed;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UseVisionDetection_Changed(object sender, RoutedEventArgs e)
    {
        RAM.SaveSettingModel();
        if (!RAM.SettingModel.UseVisionDetection)
        {
            _ = _visionDeviceClient.DisconnectAsync();
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingModel setting || !setting.VisionModuleEnabled)
        {
            return;
        }

        ConnectionStatusText.Text = "连接中...";
        bool connected = await _visionDeviceClient.ConnectAsync(
            setting.VisionDeviceIp,
            setting.VisionDevicePort,
            TimeSpan.FromSeconds(5));
        ConnectionStatusText.Text = connected ? "已连接" : "连接失败";
        if (connected)
        {
            RAM.SaveSettingModel();
        }
    }

    private void VisionDeviceClient_ConnectionClosed()
    {
        Dispatcher.BeginInvoke(() => ConnectionStatusText.Text = "连接已断开");
    }

    protected override void OnClosed(EventArgs e)
    {
        _visionDeviceClient.ConnectionClosed -= VisionDeviceClient_ConnectionClosed;
        base.OnClosed(e);
    }
}
