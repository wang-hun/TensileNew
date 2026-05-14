using System.Windows;

namespace TensileNeW;

public partial class StartupWaitWindow : Window
{
    public StartupWaitWindow(string waitText)
    {
        InitializeComponent();
        WaitTextBlock.Text = waitText;
    }

    public void SetWaitText(string waitText)
    {
        WaitTextBlock.Text = waitText;
    }
}
