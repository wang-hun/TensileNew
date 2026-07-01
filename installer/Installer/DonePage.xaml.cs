using System.Windows;
using System.Windows.Controls;

namespace EcsInstaller;

public sealed partial class DonePage : UserControl
{
    public event EventHandler? CloseRequested;

    public event EventHandler? LaunchRequested;

    public DonePage()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        LaunchRequested?.Invoke(this, EventArgs.Empty);
    }
}
