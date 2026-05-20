using System;
using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TensileNeW;

public partial class SettingsPinDialog : UserControl
{
    private const string SettingsPassword = "GB123";

    public event EventHandler? Unlocked;

    public SettingsPinDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PinBox.Focus();
    }

    private void PinBox_OnCompleted(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(PinBox.UnsafePassword, SettingsPassword, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Unlocked?.Invoke(this, EventArgs.Empty);
        CloseDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog();
    }

    private void CloseDialog()
    {
        DependencyObject? current = this;
        while (current != null)
        {
            if (current is Dialog dialog)
            {
                dialog.Close();
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }
}
