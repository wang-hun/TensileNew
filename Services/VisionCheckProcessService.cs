using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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

    private Process? _process;

    public bool IsStarted => _process is { HasExited: false };

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsStarted)
        {
            return true;
        }

        string executablePath = Path.Combine(AppContext.BaseDirectory, "Vision", "VisionCheck.exe");
        if (!File.Exists(executablePath))
        {
            return false;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        _process = process;
        await Task.Run(() => HideProcessWindowWhenReady(process, cancellationToken), cancellationToken);
        return !process.HasExited;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private static void HideProcessWindowWhenReady(Process process, CancellationToken cancellationToken)
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
            // Some GUI processes do not enter an idle state; continue with handle polling.
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!process.HasExited && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();
            IntPtr windowHandle = process.MainWindowHandle;
            if (windowHandle != IntPtr.Zero)
            {
                HideWindow(windowHandle);
                return;
            }

            Thread.Sleep(50);
        }
    }

    private static void HideWindow(IntPtr windowHandle)
    {
        ShowWindow(windowHandle, SwHide);
        IntPtr exStyle = GetWindowLongPtr(windowHandle, GwlExStyle);
        long style = exStyle.ToInt64();
        style = (style & ~WsExAppWindow) | WsExToolWindow;
        SetWindowLongPtr(windowHandle, GwlExStyle, new IntPtr(style));
        ShowWindow(windowHandle, SwHide);
    }

    private void Stop()
    {
        Process? process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                process.Dispose();
                return;
            }

            process.CloseMainWindow();
            if (!process.WaitForExit(1500) && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1500);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state checks.
        }
        catch (Exception)
        {
            // Shutdown must not be blocked by a third-party process.
        }
        finally
        {
            process.Dispose();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr value);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLong32(hWnd, nIndex);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : SetWindowLong32(hWnd, nIndex, value);
}
