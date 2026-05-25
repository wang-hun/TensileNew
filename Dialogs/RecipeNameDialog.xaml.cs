using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TensileNeW;

public partial class RecipeNameDialog : UserControl
{
    public event EventHandler<string>? Confirmed;

    public RecipeNameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Confirmed?.Invoke(this, InputBox.Text.Trim());
        CloseDialog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Ok_Click(sender, e);
        }
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
