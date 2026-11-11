using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using TensileNeW.Models;

namespace TensileNeW;

public sealed class VisionDebugSettingsWindow : Window
{
    private readonly CheckBox _enabled = new() { Content = "启用视觉软件", Margin = new Thickness(0, 4, 0, 4) };
    private readonly CheckBox _automatic = new() { Content = "二段速自动切换", Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBox _host = new();
    private readonly TextBox _port = new();
    private readonly TextBox _directory = new();
    private readonly TextBox _fileName = new();
    private readonly TextBox _interval = new();
    private readonly TextBox _timeout = new();
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly SettingModel _setting;

    public VisionDebugSettingsWindow(SettingModel setting)
    {
        _setting = setting;
        Title = "新视觉设置";
        Width = 540;
        Height = 490;
        MinWidth = 540;
        MinHeight = 490;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppSidebarBackgroundBrush");
        StackPanel panel = new() { Margin = new Thickness(20) };
        Content = panel;
        _enabled.IsChecked = setting.VisionSoftwareEnabled;
        _automatic.IsChecked = setting.TwoStageAutomaticSwitch;
        _enabled.SetResourceReference(ForegroundProperty, "AppMetricNameForegroundBrush");
        _automatic.SetResourceReference(ForegroundProperty, "AppMetricNameForegroundBrush");
        panel.Children.Add(_enabled);
        panel.Children.Add(_automatic);
        AddField(panel, "识别软件 IP", _host, setting.VisionSoftwareHost);
        AddField(panel, "端口", _port, setting.VisionSoftwarePort.ToString());
        AddField(panel, "识别图片目录（相对程序目录或绝对路径）", _directory, setting.VisionSoftwareImageDirectory);
        AddField(panel, "识别图片文件名（BMP）", _fileName, setting.VisionSoftwareImageFileName);
        AddField(panel, "拍照间隔（毫秒）", _interval, setting.VisionSoftwareCaptureIntervalMs.ToString());
        AddField(panel, "识别回复超时（毫秒）", _timeout, setting.VisionSoftwareReplyTimeoutMs.ToString());
        panel.Children.Add(_error);
        Button save = new() { Content = "保存", Width = 100, Height = 32, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        if (TryFindResource("ButtonPrimary") is Style buttonStyle) save.Style = buttonStyle;
        save.Click += Save_Click;
        panel.Children.Add(save);
    }

    private static void AddField(Panel panel, string label, TextBox box, string value)
    {
        Grid row = new() { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(ForegroundProperty, "AppMetricNameForegroundBrush");
        row.Children.Add(text);
        box.Text = value;
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        panel.Children.Add(row);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string fileName = _fileName.Text.Trim();
        if (!IPAddress.TryParse(_host.Text.Trim(), out _) || !int.TryParse(_port.Text, out int port) || port < 1 || port > 65535 ||
            !int.TryParse(_interval.Text, out int interval) || interval < 1 ||
            !int.TryParse(_timeout.Text, out int timeout) || timeout < 1 ||
            string.IsNullOrWhiteSpace(_directory.Text) || string.IsNullOrWhiteSpace(fileName) ||
            fileName != Path.GetFileName(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !fileName.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            _error.Text = "请输入有效 IP、端口、BMP 文件名和大于零的间隔与超时。";
            return;
        }
        _setting.VisionSoftwareEnabled = _enabled.IsChecked == true;
        _setting.TwoStageAutomaticSwitch = _automatic.IsChecked == true;
        _setting.VisionSoftwareHost = _host.Text.Trim();
        _setting.VisionSoftwarePort = port;
        _setting.VisionSoftwareImageDirectory = _directory.Text.Trim();
        _setting.VisionSoftwareImageFileName = fileName;
        _setting.VisionSoftwareCaptureIntervalMs = interval;
        _setting.VisionSoftwareReplyTimeoutMs = timeout;
        DialogResult = true;
    }
}
