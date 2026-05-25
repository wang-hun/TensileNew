using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TensileNeW;

public partial class RecipeConfirmDialog : UserControl
{
    public event EventHandler? Confirmed;

    public RecipeConfirmDialog()
    {
        InitializeComponent();
    }

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
