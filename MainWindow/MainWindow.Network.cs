using HandyControl.Data;
using Microsoft.Win32;
using Haukcode.HighResolutionTimer;
using NLog;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Xps.Packaging;
using TensileNeW.Models;
using TensileNeW.Services;
using TensileNeW.Tools;
using Dialog = HandyControl.Controls.Dialog;
using Growl = HandyControl.Controls.Growl;
using MessageBox = HandyControl.Controls.MessageBox;

namespace TensileNeW;


public partial class MainWindow : Window
{
    private void Home_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Home";

    private void Settings_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Settings";

    private void Help_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Help";

    private async void Playback_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CurrentPage = "Playback";
        await RefreshPlaybackTrialsAsync();
    }

    private void Variables_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Variables";

    private void ColorSchemes_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "ColorSchemes";

    private void InitializeDacuButton()
    {
        _dacuAvailable = File.Exists(GetDacuExecutablePath());
        DacuButton.Visibility = _dacuAvailable
            ? Visibility.Visible
            : Visibility.Collapsed;
        System.Windows.Controls.Grid.SetColumn(ColorSchemesButton, _dacuAvailable ? 5 : 4);
        System.Windows.Controls.Grid.SetColumn(VariablesButton, _dacuAvailable ? 6 : 5);
    }

    private static string GetDacuExecutablePath() =>
        Path.Combine(AppContext.BaseDirectory, DacuExecutableName);

    private void Dacu_Click(object sender, RoutedEventArgs e)
    {
        if (!_dacuAvailable)
        {
            return;
        }

        try
        {
            string executablePath = GetDacuExecutablePath();
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to start DACU application.");
            ShowError("DACU 程序启动失败");
        }
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        ReconnectButton.IsEnabled = false;
        IsEnabled = false;
        bool connected = false;

        var waitWindow = new StartupWaitWindow(GetConnectWaitText());

        try
        {
            waitWindow.Show();
            var reconnectTask = TryReconnectAsync();
            await Task.WhenAll(reconnectTask, Task.Delay(TimeSpan.FromSeconds(2)));
            connected = await reconnectTask;
        }
        finally
        {
            waitWindow.Close();
            IsEnabled = true;
            ReconnectButton.IsEnabled = true;
        }

        if (!connected)
        {
            ShowError("\u8fde\u63a5\u5931\u8d25\uff0c\u8bf7\u68c0\u67e5\u7ebf\u8def\uff01");
            _ = Dispatcher.BeginInvoke(ShowConnectionErrorDialog);
        }
    }

    private static string GetConnectWaitText()
    {
        return string.Equals(RAM.SettingModel.Language, "EN", StringComparison.OrdinalIgnoreCase)
            ? "Connecting to device host, please wait..."
            : "正在连接 设备主机，请稍后...";
    }

    private static async Task<bool> TryReconnectAsync()
    {
        try
        {
            return await Task.Run(DataAqc.TryReconnect);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "网络探测失败。");
            return false;
        }
    }

    /// <summary>
    /// 探测期间在单个候选 IP 上做有限次重试，覆盖刚 add address 后 Windows
    /// 路由表/源地址选择尚未就绪的窗口；超出整体探测超时立即放弃。
    /// </summary>

    private static async Task<bool> TryConnectWithRetriesAsync(Stopwatch probeStopwatch)
    {
        for (int attempt = 0; attempt < NetworkConnectRetryCount; attempt++)
        {
            TimeSpan remaining = NetworkProbeUiTimeout - probeStopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            if (await TryReconnectAsync())
            {
                return true;
            }

            if (attempt < NetworkConnectRetryCount - 1)
            {
                await Task.Delay(Min(NetworkConnectRetryInterval, remaining));
            }
        }

        return false;
    }

    private static bool IsNetworkProbeTimedOut(Stopwatch probeStopwatch) =>
        probeStopwatch.Elapsed >= NetworkProbeUiTimeout;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private void ShowConnectionErrorDialog()
    {
        Dialog.Show(new ConnectionErrorDialog(RunNetworkProbeAndReconnectAsync));
    }

    private async Task RunNetworkProbeAndReconnectAsync()
    {
        if (_networkProbeRunning)
        {
            return;
        }

        _networkProbeRunning = true;
        ReconnectButton.IsEnabled = false;
        IsEnabled = false;
        string? successMessage = null;
        string? warningMessage = null;
        string? errorMessage = null;
        bool showFailureDialog = false;

        var waitWindow = new StartupWaitWindow("正在探测有线网络并尝试连接设备，请稍后...");
        DataAqc.AutoReconnectSuspended = true;
        try
        {
            waitWindow.Show();
            IReadOnlyList<NetworkProbeCandidate> candidates =
                NetworkAdapterProbeService.BuildProbeCandidates(RAM.SettingModel.PLC_IP);
            if (candidates.Count == 0)
            {
                warningMessage = "未发现可用于探测的有线网卡。";
            }
            else
            {
                Stopwatch probeStopwatch = Stopwatch.StartNew();
                NetworkProbeResult? lastFailure = null;
                foreach (NetworkProbeCandidate candidate in candidates)
                {
                    if (IsNetworkProbeTimedOut(probeStopwatch))
                    {
                        warningMessage = "网络探测超时，请检查设备线路后重试。";
                        break;
                    }

                    waitWindow.SetWaitText($"正在配置 {candidate.AdapterName}，请稍后...");
                    NetworkProbeResult addResult = await NetworkAdapterProbeService.RunElevatedAddAddressAsync(candidate);
                    if (!addResult.Success)
                    {
                        lastFailure = addResult;
                        continue;
                    }

                    if (IsNetworkProbeTimedOut(probeStopwatch))
                    {
                        warningMessage = "网络探测超时，请检查设备线路后重试。";
                        waitWindow.SetWaitText($"正在清理 {candidate.AdapterName}，请稍后...");
                        await NetworkAdapterProbeService.RunElevatedRemoveAddressAsync(candidate);
                        break;
                    }

                    TimeSpan remainingDelay = NetworkProbeUiTimeout - probeStopwatch.Elapsed;
                    if (remainingDelay <= TimeSpan.Zero)
                    {
                        warningMessage = "网络探测超时，请检查设备线路后重试。";
                        waitWindow.SetWaitText($"正在清理 {candidate.AdapterName}，请稍后...");
                        await NetworkAdapterProbeService.RunElevatedRemoveAddressAsync(candidate);
                        break;
                    }

                    await Task.Delay(Min(NetworkAddressApplyDelay, remainingDelay));
                    waitWindow.SetWaitText($"正在通过 {candidate.AdapterName} 连接设备...");
                    bool connected = await TryConnectWithRetriesAsync(probeStopwatch);
                    if (connected)
                    {
                        // 探测重连期间采集已暂停；等待配方写入完成后再恢复采集。
                        await WriteStartupRecipeAsync();
                        successMessage = $"网络检查成功，已通过 {candidate.AdapterName} 连接设备。";
                        break;
                    }

                    lastFailure = new NetworkProbeResult
                    {
                        Success = false,
                        AdapterName = candidate.AdapterName,
                        LocalIp = candidate.LocalIp,
                        Message = $"已配置 {candidate.AdapterName}，但设备连接失败。"
                    };

                    waitWindow.SetWaitText($"正在清理 {candidate.AdapterName}，请稍后...");
                    await NetworkAdapterProbeService.RunElevatedRemoveAddressAsync(candidate);
                    if (IsNetworkProbeTimedOut(probeStopwatch))
                    {
                        warningMessage = "网络探测超时，请检查设备线路后重试。";
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(successMessage) && string.IsNullOrWhiteSpace(warningMessage))
                {
                    errorMessage = lastFailure?.Message ?? "所有有线网卡均未连接到设备。";
                    showFailureDialog = true;
                }
            }
        }
        finally
        {
            DataAqc.AutoReconnectSuspended = false;
            if (waitWindow.IsVisible)
            {
                waitWindow.Close();
            }

            IsEnabled = true;
            ReconnectButton.IsEnabled = true;
            _networkProbeRunning = false;
        }

        if (!string.IsNullOrWhiteSpace(successMessage))
        {
            ShowSuccess(successMessage);
        }

        if (!string.IsNullOrWhiteSpace(warningMessage))
        {
            ShowWarning(warningMessage);
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            ShowError(errorMessage);
        }

        if (showFailureDialog)
        {
            _ = Dispatcher.BeginInvoke(ShowConnectionErrorDialog);
        }
    }

}