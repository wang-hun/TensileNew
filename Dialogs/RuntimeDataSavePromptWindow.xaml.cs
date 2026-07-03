using System.Windows;
using System.Windows.Input;

namespace TensileNeW;

public partial class RuntimeDataSavePromptWindow : Window
{
    public RuntimeDataSavePromptWindow()
    {
        InitializeComponent();
    }

    public bool ShouldSave { get; private set; }
    public bool HasDecision { get; private set; }
    public bool DontAskAgain => DontAskAgainCheckBox.IsChecked == true;

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        HasDecision = true;
        ShouldSave = true;
        DialogResult = true;
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        HasDecision = true;
        ShouldSave = false;
        DialogResult = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
