using System.Windows;

namespace TensileNeW;

public partial class VariableWindow : Window
{
    public event EventHandler? VisionSettingsRequested;

    private void VisionSettings_Click(object sender, RoutedEventArgs e) => VisionSettingsRequested?.Invoke(this, EventArgs.Empty);

    public VariableWindow()
    {
        InitializeComponent();
    }
}
