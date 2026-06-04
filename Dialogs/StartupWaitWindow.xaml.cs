using System.Windows;

namespace TensileNeW;

public partial class StartupWaitWindow : Window
{
    public StartupWaitWindow(string waitText, bool showHint = false)
    {
        InitializeComponent();
        WaitTextBlock.Text = waitText;
        HintTextBlock.Visibility = showHint ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetWaitText(string waitText)
    {
        WaitTextBlock.Text = waitText;
    }

    public void SetHintVisibility(bool isVisible)
    {
        HintTextBlock.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
