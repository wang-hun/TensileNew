using System;
using System.Windows;

namespace TensileNeW;

public partial class SettingsPinWindow : Window
{
    public event EventHandler? Unlocked;
    public event EventHandler? ColorUnlocked;
    public event EventHandler? DebugAlgorithmDataRequested;
    public event EventHandler? VisionModuleEnableRequested;
    public event EventHandler? VisionModuleDisableRequested;

    public SettingsPinWindow()
    {
        InitializeComponent();
        PinDialog.Unlocked += (_, _) => Unlocked?.Invoke(this, EventArgs.Empty);
        PinDialog.ColorUnlocked += (_, _) => ColorUnlocked?.Invoke(this, EventArgs.Empty);
        PinDialog.DebugAlgorithmDataRequested += (_, _) => DebugAlgorithmDataRequested?.Invoke(this, EventArgs.Empty);
        PinDialog.VisionModuleEnableRequested += (_, _) => VisionModuleEnableRequested?.Invoke(this, EventArgs.Empty);
        PinDialog.VisionModuleDisableRequested += (_, _) => VisionModuleDisableRequested?.Invoke(this, EventArgs.Empty);
    }
}
