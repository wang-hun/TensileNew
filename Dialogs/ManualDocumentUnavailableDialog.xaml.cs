using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TensileNeW;

public partial class ManualDocumentUnavailableDialog : UserControl
{
    public ManualDocumentUnavailableDialog()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
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
