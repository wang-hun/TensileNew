using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

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

    public async Task SetWaitTextAsync(string waitText)
    {
        SetWaitText(waitText);
        WaitTextBlock.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(50);
    }

    public void SetHintVisibility(bool isVisible)
    {
        HintTextBlock.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
