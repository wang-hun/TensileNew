using System.IO;
using System.Windows;
using System.Windows.Controls;
using TensileNeW.Models;

namespace TensileNeW;

public sealed class VisionDebugSettingsWindow : Window
{
    private readonly CheckBox _enabled = new() { Content = "启用 RingInspect 图像检测", Margin = new Thickness(0, 4, 0, 4) };
    private readonly CheckBox _automatic = new() { Content = "二段速自动切换", Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBox _directory = new();
    private readonly TextBox _fileName = new();
    private readonly TextBox _timeout = new();
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly SettingModel _setting;

    public VisionDebugSettingsWindow(SettingModel setting)
    {
        _setting = setting;
        Title = "新视觉设置";
        Width = 540;
        Height = 430;
        MinWidth = 540;
        MinHeight = 430;
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
        TextBlock apiInfo = new()
        {
            Text = "检测由主程序内嵌托管，调用 POST /api/detect/strict；缺陷数大于 0 时触发停机。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 8)
        };
        apiInfo.SetResourceReference(ForegroundProperty, "AppMetricNameForegroundBrush");
        panel.Children.Add(apiInfo);
        AddField(panel, "\u8bc6\u522b\u56fe\u7247\u76ee\u5f55", _directory, setting.VisionSoftwareImageDirectory);
        AddField(panel, "\u8bc6\u522b\u56fe\u7247\u6587\u4ef6\u540d\uff08BMP\uff09", _fileName, setting.VisionSoftwareImageFileName);
        AddField(panel, "检测超时（毫秒）", _timeout, Math.Max(100, setting.VisionSoftwareReplyTimeoutMs).ToString());
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
        if (!int.TryParse(_timeout.Text, out int timeout) || timeout < 100)
        {
            _error.Text = "\u8bf7\u8f93\u5165\u4e0d\u5c0f\u4e8e 100 \u6beb\u79d2\u7684\u68c0\u6d4b\u8d85\u65f6\u3002";
            return;
        }
        if (string.IsNullOrWhiteSpace(_directory.Text))
        {
            _error.Text = "\u8bf7\u8f93\u5165\u8bc6\u522b\u56fe\u7247\u76ee\u5f55\u3002";
            return;
        }
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !fileName.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            _error.Text = "\u8bf7\u8f93\u5165\u6709\u6548 BMP \u6587\u4ef6\u540d\uff08\u4ec5\u6587\u4ef6\u540d\uff0c\u6269\u5c55\u540d\u4e3a .bmp\uff09\u3002";
            return;
        }
        _setting.VisionSoftwareEnabled = _enabled.IsChecked == true;
        _setting.TwoStageAutomaticSwitch = _automatic.IsChecked == true;
        _setting.VisionSoftwareImageDirectory = _directory.Text.Trim();
        _setting.VisionSoftwareImageFileName = fileName;
        _setting.VisionSoftwareReplyTimeoutMs = timeout;
        DialogResult = true;
    }
}