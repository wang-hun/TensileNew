using System.Diagnostics;
using System.IO;
using NLog;
using TensileNeW;

namespace TensileNeW.Services;

public sealed class RingInspectHostService : IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly string _executablePath = Path.Combine(AppContext.BaseDirectory, "RingInspect", "RingInspect.Wpf.exe");
    private Process? _process;
    private IntPtr _windowHandle;
    private RingInspectHostWindow? _hostWindow;

    public bool IsAvailable => _process is { HasExited: false } && _windowHandle != IntPtr.Zero;

    public async Task<bool> StartAsync()
    {
        if (IsAvailable) return true;
        if (!File.Exists(_executablePath))
        {
            Logger.Error("RingInspect recognition executable is missing: {0}", _executablePath);
            return false;
        }

        try
        {
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = _executablePath,
                WorkingDirectory = Path.GetDirectoryName(_executablePath)!,
                UseShellExecute = false
            });
            if (_process is null) throw new InvalidOperationException("The recognition process could not be started.");

            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline && !_process.HasExited)
            {
                _process.Refresh();
                _windowHandle = _process.MainWindowHandle;
                if (_windowHandle != IntPtr.Zero)
                {
                    RingInspectNative.HideWindow(_windowHandle);
                    Logger.Info("RingInspect recognition process started and hidden: {0}", _windowHandle);
                    return true;
                }
                await Task.Delay(100);
            }

            throw new TimeoutException("The recognition window did not become available within 15 seconds.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to start or locate the RingInspect recognition window.");
            DisposeProcess();
            return false;
        }
    }

    public bool Show(MainWindow owner)
    {
        if (!IsAvailable) return false;
        if (_hostWindow is null)
        {
            _hostWindow = new RingInspectHostWindow(owner, _process!, _windowHandle);
            _process!.EnableRaisingEvents = true;
            _process.Exited += Process_Exited;
        }
        _hostWindow.ShowOrActivate();
        return true;
    }

    private void Process_Exited(object? sender, EventArgs e)
    {
        Logger.Warn("RingInspect recognition process exited.");
        _windowHandle = IntPtr.Zero;
        _hostWindow?.Dispatcher.BeginInvoke(new Action(_hostWindow.Hide));
    }

    public void Dispose()
    {
        _hostWindow?.ShutdownHost();
        _hostWindow = null;
        DisposeProcess();
    }

    private void DisposeProcess()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Failed to stop the RingInspect recognition process during ECS shutdown.");
        }
        _process.Dispose();
        _process = null;
        _windowHandle = IntPtr.Zero;
    }
}