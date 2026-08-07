using System.Threading.Tasks;
using System.Threading;
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

/// <summary>
/// Hosts a startup wait window on a dedicated STA dispatcher so its animation
/// is independent of the main window's synchronous UI work.
/// </summary>
public sealed class BackgroundStartupWaitWindow : IDisposable
{
    private readonly string _waitText;
    private readonly bool _showHint;
    private readonly TaskCompletionSource<bool> _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private StartupWaitWindow? _window;

    public BackgroundStartupWaitWindow(string waitText, bool showHint = false)
    {
        _waitText = waitText;
        _showHint = showHint;
    }

    public Task ShowAsync()
    {
        if (_thread != null)
        {
            return _shown.Task;
        }

        _thread = new Thread(RunWindow)
        {
            IsBackground = true
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        return _shown.Task;
    }

    public void Dispose()
    {
        StartupWaitWindow? window = _window;
        if (window == null)
        {
            return;
        }

        try
        {
            window.Dispatcher.Invoke(() =>
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RunWindow()
    {
        try
        {
            StartupWaitWindow window = new(_waitText, _showHint);
            _window = window;
            window.Closed += (_, _) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            window.Show();
            _shown.TrySetResult(true);
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            _shown.TrySetException(ex);
        }
    }
}
