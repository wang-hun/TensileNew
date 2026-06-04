using System.Windows;
using TensileNeW.Models;

namespace TensileNeW;

public partial class ChartSettingsWindow : Window
{
    private readonly Action _saveSettings;
    private bool _initialized;

    public ChartSettingsWindow(SettingModel setting, Action saveSettings)
    {
        InitializeComponent();
        DataContext = setting;
        _saveSettings = saveSettings;
        _initialized = true;
    }

    public event EventHandler? Confirmed;
    public event EventHandler? SettingsChanged;
    public event EventHandler? ResetSerialRequested;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void SettingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        _saveSettings();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ResetSerial_Click(object sender, RoutedEventArgs e)
    {
        ResetSerialRequested?.Invoke(this, EventArgs.Empty);
    }
}
