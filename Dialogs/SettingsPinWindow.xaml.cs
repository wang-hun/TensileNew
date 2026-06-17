using System;
using System.Windows;

namespace TensileNeW;

public partial class SettingsPinWindow : Window
{
    public event EventHandler? Unlocked;
    public event EventHandler? ColorUnlocked;

    public SettingsPinWindow()
    {
        InitializeComponent();
        PinDialog.Unlocked += (_, _) => Unlocked?.Invoke(this, EventArgs.Empty);
        PinDialog.ColorUnlocked += (_, _) => ColorUnlocked?.Invoke(this, EventArgs.Empty);
    }
}
