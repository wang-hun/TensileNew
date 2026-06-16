using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TensileNeW;

public partial class ConnectionErrorDialog : UserControl
{
    private readonly Func<Task>? _networkProbeRequested;

    public ConnectionErrorDialog(Func<Task>? networkProbeRequested = null)
    {
        _networkProbeRequested = networkProbeRequested;
        InitializeComponent();
    }

    private async void NetworkProbe_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog();
        if (_networkProbeRequested is not null)
        {
            await _networkProbeRequested();
        }
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
