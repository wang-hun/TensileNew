using System.Diagnostics;
using System.IO;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using NLog;
using TensileNeW;

namespace TensileNeW.Services;

public sealed class RingInspectHostService : IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const int HostedWindowReadyMessage = 0x8431;
    private const int WsPopup = unchecked((int)0x80000000);
    private readonly string _executablePath = Path.Combine(AppContext.BaseDirectory, "RingInspect", "RingInspect.Wpf.exe");
    private Process? _process;
    private HwndSource? _bootstrapSource;
    private TaskCompletionSource<IntPtr>? _windowReady;
    private IntPtr _windowHandle;
    private RingInspectHostWindow? _hostWindow;

    public bool IsAvailable => _process is { HasExited: false } && _windowHandle != IntPtr.Zero;

    public async Task<bool> StartAsync()
    {
        if (IsAvailable) return true;
        if (_process is not null) Dispose();
        if (!File.Exists(_executablePath))
        {
            Logger.Error("RingInspect recognition executable is missing: {0}", _executablePath);
            return false;
        }

        try
        {
            _windowReady = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
            HwndSourceParameters parameters = new("RingInspectBootstrap")
            {
                Width = 1,
                Height = 1,
                WindowStyle = WsPopup
            };
            _bootstrapSource = new HwndSource(parameters);
            _bootstrapSource.AddHook(BootstrapWindowProc);

            ProcessStartInfo startInfo = new()
            {
                FileName = _executablePath,
                WorkingDirectory = Path.GetDirectoryName(_executablePath)!,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add($"--host-hwnd=0x{_bootstrapSource.Handle.ToInt64():X}");
            _process = Process.Start(startInfo);
            if (_process is null) throw new InvalidOperationException("The recognition process could not be started.");

            Task<IntPtr> readyTask = _windowReady.Task;
            Task completed = await Task.WhenAny(readyTask, Task.Delay(TimeSpan.FromSeconds(20)), _process.WaitForExitAsync());
            if (completed != readyTask)
                throw new TimeoutException("The recognition application did not finish startup and signal its hosted window within 20 seconds.");

            _windowHandle = await readyTask;
            _process.EnableRaisingEvents = true;
            _process.Exited += Process_Exited;
            Logger.Info("RingInspect recognition process started in optional hosted mode: {0}", _windowHandle);
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to start or locate the RingInspect recognition window.");
            DisposeProcess();
            return false;
        }
    }

    private IntPtr BootstrapWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != HostedWindowReadyMessage || _process is null || _bootstrapSource is null) return IntPtr.Zero;
        IntPtr candidate = wParam;
        if (!RingInspectNative.IsWindow(candidate) || RingInspectNative.GetParent(candidate) != _bootstrapSource.Handle) return IntPtr.Zero;
        _ = GetWindowThreadProcessId(candidate, out uint processId);
        if (processId != (uint)_process.Id) return IntPtr.Zero;
        handled = true;
        _windowReady?.TrySetResult(candidate);
        return new IntPtr(1);
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    public bool Prepare(MainWindow owner)
    {
        if (!owner.IsLoaded || !owner.IsVisible)
        {
            Logger.Warn("Cannot prepare RingInspect host before the ECS main window is visible.");
            return false;
        }
        if (!IsAvailable || !RingInspectNative.IsWindow(_windowHandle)) return false;
        try
        {
            _hostWindow = new RingInspectHostWindow(owner, _process!, _windowHandle);
            _hostWindow.PrepareHidden();
            if (!_hostWindow.IsEmbedded) throw new InvalidOperationException("识别程序窗口未嵌入 ECS 宿主。");
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "RingInspect host could not embed the recognition window.");
            _hostWindow?.ShutdownHost();
            _hostWindow = null;
            DisposeProcess();
            return false;
        }
    }

    public bool Show(MainWindow owner)
    {
        if (!IsAvailable || !RingInspectNative.IsWindow(_windowHandle)) return false;
        if (_hostWindow is null && !Prepare(owner)) return false;
        try
        {
            return _hostWindow!.ShowOrActivate();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "RingInspect host could not display the recognition window.");
            return false;
        }
    }

    private void Process_Exited(object? sender, EventArgs e)
    {
        Logger.Warn("RingInspect recognition process exited.");
        _windowHandle = IntPtr.Zero;
        _hostWindow?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _hostWindow?.ShutdownHost();
            _hostWindow = null;
        }));
    }

    public void Dispose()
    {
        _hostWindow?.ShutdownHost();
        _hostWindow = null;
        DisposeProcess();
    }

    private void DisposeProcess()
    {
        if (_process is not null)
        {
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
        }
        _windowHandle = IntPtr.Zero;
        _windowReady = null;
        _bootstrapSource?.RemoveHook(BootstrapWindowProc);
        _bootstrapSource?.Dispose();
        _bootstrapSource = null;
    }
}
