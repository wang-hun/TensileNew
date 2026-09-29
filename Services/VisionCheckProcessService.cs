using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace TensileNeW.Services;

/// <summary>
/// Starts the bundled VisionCheck process without exposing its UI or taskbar button.
/// Only the process started by this instance is managed and stopped on shutdown.
/// </summary>
public sealed class VisionCheckProcessService : IDisposable
{
    private const int SwHide = 0;
    private const int GwlExStyle = -20;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint CreateNoWindow = 0x08000000;
    private const uint StartfUseShowWindow = 0x00000001;

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private Process? _process;
    private CancellationTokenSource? _windowHiderCancellation;
    private Task? _windowHiderTask;
    private static readonly IntPtr HwndBottom = new(1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpHideWindow = 0x0080;

    public bool IsStarted => _process is { HasExited: false };

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsStarted)
        {
            Logger.Info("VisionCheck is already running, PID={0}.", _process!.Id);
            return true;
        }

        string executablePath = Path.Combine(AppContext.BaseDirectory, "Vision", "VisionCheck.exe");
        if (!File.Exists(executablePath))
        {
            Logger.Info("VisionCheck executable not found: {0}", executablePath);
            return false;
        }

        Logger.Info("Starting VisionCheck hidden: {0}", executablePath);
        Process? process = StartHiddenProcess(executablePath);
        if (process is null)
        {
            Logger.Error("VisionCheck failed to start: {0}", executablePath);
            return false;
        }

        _process = process;
        Logger.Info("VisionCheck process started, PID={0}.", process.Id);
        CancellationTokenSource windowHiderCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _windowHiderCancellation = windowHiderCancellation;
        _windowHiderTask = Task.Run(
            () => HideProcessWindowsLoop(process, windowHiderCancellation.Token),
            windowHiderCancellation.Token);
        await Task.Run(() => WaitForInputIdle(process), cancellationToken);
        await Task.Delay(200, cancellationToken);
        Logger.Info(
            "VisionCheck startup handling completed, PID={0}, running={1}, hidden-window-monitor=active.",
            process.Id,
            !process.HasExited);
        return !process.HasExited;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private static Process? StartHiddenProcess(string executablePath)
    {
        STARTUPINFO startupInfo = new()
        {
            cb = Marshal.SizeOf<STARTUPINFO>(),
            dwFlags = StartfUseShowWindow,
            wShowWindow = SwHide
        };
        StringBuilder commandLine = new($"\"{executablePath}\"");

        bool created = CreateProcess(
            executablePath,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            CreateNoWindow,
            IntPtr.Zero,
            Path.GetDirectoryName(executablePath),
            ref startupInfo,
            out PROCESS_INFORMATION processInformation);
        if (!created)
        {
            Logger.Error("CreateProcess failed for VisionCheck, Win32Error={0}.", Marshal.GetLastWin32Error());
            return null;
        }

        CloseHandle(processInformation.hThread);
        CloseHandle(processInformation.hProcess);
        return Process.GetProcessById((int)processInformation.dwProcessId);
    }

    private static void WaitForInputIdle(Process process)
    {
        try
        {
            process.WaitForInputIdle(3000);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (TimeoutException)
        {
            // Some GUI processes do not enter an idle state.
        }
    }

    private static void HideProcessWindowsLoop(Process process, CancellationToken cancellationToken)
    {
        HashSet<IntPtr> loggedWindowHandles = [];
        while (!process.HasExited && !cancellationToken.IsCancellationRequested)
        {
            IntPtr[] windowHandles = FindTopLevelWindows(process.Id);
            foreach (IntPtr windowHandle in windowHandles)
            {
                HideWindow(windowHandle);
                if (loggedWindowHandles.Add(windowHandle))
                {
                    Logger.Info("Hidden VisionCheck window, handle={0}, PID={1}.", windowHandle, process.Id);
                }
            }

            Thread.Sleep(50);
        }
    }

    private static IntPtr[] FindTopLevelWindows(int processId)
    {
        List<IntPtr> windowHandles = [];
        EnumWindows((windowHandle, _) =>
        {
            GetWindowThreadProcessId(windowHandle, out uint ownerProcessId);
            if (ownerProcessId == processId)
            {
                windowHandles.Add(windowHandle);
            }

            return true;
        }, IntPtr.Zero);
        return windowHandles.ToArray();
    }

    private static void HideWindow(IntPtr windowHandle)
    {
        ShowWindow(windowHandle, SwHide);
        IntPtr exStyle = GetWindowLongPtr(windowHandle, GwlExStyle);
        long style = exStyle.ToInt64();
        style = (style & ~WsExAppWindow) | WsExToolWindow;
        SetWindowLongPtr(windowHandle, GwlExStyle, new IntPtr(style));
        SetWindowPos(
            windowHandle,
            HwndBottom,
            0,
            0,
            0,
            0,
            SwpNoActivate | SwpNoMove | SwpNoSize | SwpFrameChanged | SwpHideWindow);
    }

    private void Stop()
    {
        Process? process = _process;
        _process = null;
        CancellationTokenSource? windowHiderCancellation = _windowHiderCancellation;
        _windowHiderCancellation = null;
        Task? windowHiderTask = _windowHiderTask;
        _windowHiderTask = null;
        windowHiderCancellation?.Cancel();
        try
        {
            windowHiderTask?.Wait(500);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "VisionCheck window hider did not stop cleanly.");
        }
        finally
        {
            windowHiderCancellation?.Dispose();
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                Logger.Info("VisionCheck process already exited, PID={0}.", process.Id);
                return;
            }

            Logger.Info("Stopping VisionCheck because the application is exiting, PID={0}.", process.Id);
            process.CloseMainWindow();
            if (!process.WaitForExit(1500) && !process.HasExited)
            {
                Logger.Warn("VisionCheck did not exit within the grace period; killing process tree, PID={0}.", process.Id);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1500);
            }

            Logger.Info("VisionCheck process stopped, PID={0}.", process.Id);
        }
        catch (InvalidOperationException)
        {
            Logger.Info("VisionCheck exited while shutdown was in progress, PID={0}.", process.Id);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Exception while stopping VisionCheck; process may still be running, PID={0}.", process.Id);
        }
        finally
        {
            process.Dispose();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string? applicationName,
        StringBuilder? commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLong32(hWnd, nIndex);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : SetWindowLong32(hWnd, nIndex, value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }
}
