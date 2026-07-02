using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TensileNeW;

public partial class ClearPlotConfirmDialog : UserControl
{
    public ClearPlotConfirmDialog()
    {
        InitializeComponent();
    }

    public event EventHandler? Confirmed;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
        CloseDialog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
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
