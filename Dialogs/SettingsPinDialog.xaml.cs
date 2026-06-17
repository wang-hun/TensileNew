using System;
using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TensileNeW;

public partial class SettingsPinDialog : UserControl
{
    private const string SettingsPassword = "GB123";
    private const string ColorPassword = "COLOR";

    public event EventHandler? Unlocked;
    public event EventHandler? ColorUnlocked;

    public SettingsPinDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PinBox.Focus();
    }

    private void PinBox_OnCompleted(object sender, RoutedEventArgs e)
    {
        if (string.Equals(PinBox.UnsafePassword, SettingsPassword, StringComparison.OrdinalIgnoreCase))
        {
            Unlocked?.Invoke(this, EventArgs.Empty);
            CloseDialog();
            return;
        }

        if (string.Equals(PinBox.UnsafePassword, ColorPassword, StringComparison.OrdinalIgnoreCase))
        {
            ColorUnlocked?.Invoke(this, EventArgs.Empty);
            CloseDialog();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog();
    }

    private void CloseDialog()
    {
        System.Windows.Window? ownerWindow = System.Windows.Window.GetWindow(this);

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

        ownerWindow?.Close();
    }
}
