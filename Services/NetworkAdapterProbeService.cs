using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;

namespace TensileNeW.Services;

public sealed class NetworkProbeResult
{
    public bool Success { get; set; }
    public string? AdapterName { get; set; }
    public string? AdapterDescription { get; set; }
    public string? LocalIp { get; set; }
    public string? Message { get; set; }
}

public sealed class NetworkProbeCandidate
{
    public required string AdapterName { get; init; }
    public string? AdapterDescription { get; init; }
    public required string LocalIp { get; init; }
}

public static class NetworkAdapterProbeService
{
    private const string ProbeArg = "--network-probe";
    private const string AddAddressCommand = "add-address";
    private const string RemoveAddressCommand = "remove-address";
    private static readonly TimeSpan AddressCommandTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan NetshCommandTimeout = TimeSpan.FromSeconds(5);
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static bool IsProbeWorker(string[] args) =>
        args.Length >= 5 && string.Equals(args[0], ProbeArg, StringComparison.OrdinalIgnoreCase);

    public static int RunProbeWorker(string[] args)
    {
        try
        {
            string command = args[1];
            string resultPath = args[2];
            string adapterName = args[3];
            string localIp = args[4];

            NetworkProbeResult result = command switch
            {
                AddAddressCommand => AddAddress(adapterName, localIp),
                RemoveAddressCommand => RemoveAddress(adapterName, localIp),
                _ => new NetworkProbeResult
                {
                    Success = false,
                    Message = $"未知网络配置命令：{command}"
                }
            };

            WriteProbeResult(resultPath, result);
            return result.Success ? 0 : 2;
        }
        catch (Exception ex)
        {
            TryWriteFailureResult(args, ex.Message);
            return 1;
        }
    }

    public static bool HasSameSubnetWiredAddress(string plcIp)
    {
        if (!IPAddress.TryParse(plcIp, out IPAddress? targetIp) ||
            targetIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        return EnumerateWiredAdapters()
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Any(address => IsInSameSubnet(address.Address, targetIp, address.PrefixLength));
    }

    public static IReadOnlyList<NetworkProbeCandidate> BuildProbeCandidates(string plcIp)
    {
        if (!IPAddress.TryParse(plcIp, out IPAddress? targetIp) ||
            targetIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return [];
        }

        List<NetworkProbeCandidate> candidates = [];
        HashSet<IPAddress> existingAddresses = GetAllLocalIPv4Addresses();

        foreach (NetworkInterface adapter in EnumerateWiredAdapters())
        {
            // Do not reconfigure an adapter that already has an active TCP
            // connection bound to one of its local addresses. That adapter is
            // being used by another device/application and must be left alone;
            // probing it can leave the whole network-probe sequence waiting on
            // an unrelated connection.
            if (IsAdapterInUse(adapter))
            {
                continue;
            }

            foreach (IPAddress localIp in BuildCandidateLocalIps(targetIp, existingAddresses))
            {
                candidates.Add(new NetworkProbeCandidate
                {
                    AdapterName = adapter.Name,
                    AdapterDescription = adapter.Description,
                    LocalIp = localIp.ToString()
                });
            }
        }

        return candidates;
    }

    private static bool IsAdapterInUse(NetworkInterface adapter)
    {
        HashSet<IPAddress> adapterAddresses = adapter.GetIPProperties().UnicastAddresses
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .ToHashSet();

        if (adapterAddresses.Count == 0)
        {
            return false;
        }

        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpConnections()
                .Where(connection => connection.State != TcpState.Closed)
                .Any(connection => adapterAddresses.Contains(connection.LocalEndPoint.Address));
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "无法读取网卡 {0} 的活动 TCP 连接，将继续探测。", adapter.Name);
            return false;
        }
    }

    public static Task<NetworkProbeResult> RunElevatedAddAddressAsync(NetworkProbeCandidate candidate) =>
        RunElevatedAddressCommandAsync(AddAddressCommand, candidate);

    public static Task<NetworkProbeResult> RunElevatedRemoveAddressAsync(NetworkProbeCandidate candidate) =>
        RunElevatedAddressCommandAsync(RemoveAddressCommand, candidate);

    private static async Task<NetworkProbeResult> RunElevatedAddressCommandAsync(string command, NetworkProbeCandidate candidate)
    {
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            return new NetworkProbeResult
            {
                Success = false,
                Message = "无法找到当前程序路径，不能启动网络配置进程。"
            };
        }

        string resultPath = Path.Combine(Path.GetTempPath(), $"TensileNeW-NetworkProbe-{Guid.NewGuid():N}.json");
        try
        {
            using Process process = StartElevatedAddressProcess(processPath, command, resultPath, candidate);
            Task exitTask = process.WaitForExitAsync();
            Task completedTask = await Task.WhenAny(exitTask, Task.Delay(AddressCommandTimeout));
            if (completedTask != exitTask)
            {
                TryKillProcess(process);
                return new NetworkProbeResult
                {
                    Success = false,
                    Message = "网络配置超时，请检查系统网络设置后重试。"
                };
            }

            if (!File.Exists(resultPath))
            {
                return new NetworkProbeResult
                {
                    Success = false,
                    Message = "网络配置未返回结果。"
                };
            }

            string json = await File.ReadAllTextAsync(resultPath);
            NetworkProbeResult? result = JsonSerializer.Deserialize<NetworkProbeResult>(json);
            return result ?? new NetworkProbeResult
            {
                Success = false,
                Message = "网络配置结果无效。"
            };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Logger.Warn(ex, "用户取消管理员权限请求。");
            return new NetworkProbeResult
            {
                Success = false,
                Message = "已取消管理员权限请求。"
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "启动网络配置失败。");
            return new NetworkProbeResult
            {
                Success = false,
                Message = ex.Message
            };
        }
        finally
        {
            TryDeleteResultFile(resultPath);
        }
    }

    private static Process StartElevatedAddressProcess(
        string processPath,
        string command,
        string resultPath,
        NetworkProbeCandidate candidate)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = processPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (!IsAdministrator())
        {
            startInfo.Verb = "runas";
        }

        startInfo.ArgumentList.Add(ProbeArg);
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add(resultPath);
        startInfo.ArgumentList.Add(candidate.AdapterName);
        startInfo.ArgumentList.Add(candidate.LocalIp);

        return Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动网络配置进程。");
    }

    private static NetworkProbeResult AddAddress(string adapterName, string localIp)
    {
        CommandResult addResult = RunNetsh("interface", "ipv4", "add", "address",
            $"name={adapterName}",
            $"address={localIp}",
            "mask=255.255.255.0");

        return new NetworkProbeResult
        {
            Success = addResult.Success,
            AdapterName = adapterName,
            LocalIp = localIp,
            Message = addResult.Success
                ? $"已在 {adapterName} 上添加 {localIp}。"
                : $"添加额外 IP 失败：{addResult.Output}"
        };
    }

    private static NetworkProbeResult RemoveAddress(string adapterName, string localIp)
    {
        CommandResult removeResult = RunNetsh("interface", "ipv4", "delete", "address",
            $"name={adapterName}",
            $"address={localIp}");

        return new NetworkProbeResult
        {
            Success = removeResult.Success,
            AdapterName = adapterName,
            LocalIp = localIp,
            Message = removeResult.Success
                ? $"已从 {adapterName} 移除 {localIp}。"
                : $"移除额外 IP 失败：{removeResult.Output}"
        };
    }

    private static IEnumerable<NetworkInterface> EnumerateWiredAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .Where(adapter => adapter.NetworkInterfaceType is
                NetworkInterfaceType.Ethernet or
                NetworkInterfaceType.FastEthernetFx or
                NetworkInterfaceType.FastEthernetT or
                NetworkInterfaceType.GigabitEthernet)
            .Where(adapter => !adapter.Description.Contains("wi-fi", StringComparison.OrdinalIgnoreCase))
            .Where(adapter => !adapter.Description.Contains("wireless", StringComparison.OrdinalIgnoreCase))
            .Where(adapter => !adapter.Name.Contains("wi-fi", StringComparison.OrdinalIgnoreCase))
            .Where(adapter => !adapter.Name.Contains("wireless", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<IPAddress> BuildCandidateLocalIps(IPAddress targetIp, HashSet<IPAddress> existingAddresses)
    {
        byte[] bytes = targetIp.GetAddressBytes();
        int[] hostCandidates = [200, 201, 202, 203, 204, 205, 100, 101, 102, 103, 104, 105, 210, 211, 212, 213, 214, 215];

        foreach (int host in hostCandidates)
        {
            if (bytes[3] == host)
            {
                continue;
            }

            IPAddress candidate = new(new[] { bytes[0], bytes[1], bytes[2], (byte)host });
            if (!existingAddresses.Contains(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static bool IsInSameSubnet(IPAddress localIp, IPAddress targetIp, int prefixLength)
    {
        if (prefixLength is <= 0 or > 32)
        {
            return false;
        }

        uint local = ToUInt32(localIp);
        uint target = ToUInt32(targetIp);
        uint mask = prefixLength == 32 ? uint.MaxValue : uint.MaxValue << (32 - prefixLength);
        return (local & mask) == (target & mask);
    }

    private static uint ToUInt32(IPAddress ip)
    {
        byte[] bytes = ip.GetAddressBytes();
        return ((uint)bytes[0] << 24) |
               ((uint)bytes[1] << 16) |
               ((uint)bytes[2] << 8) |
               bytes[3];
    }

    private static HashSet<IPAddress> GetAllLocalIPv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(address => address.Address)
            .ToHashSet();
    }

    private static CommandResult RunNetsh(params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "netsh",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 netsh。");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)NetshCommandTimeout.TotalMilliseconds))
        {
            TryKillProcess(process);
            return new CommandResult(false, "netsh 执行超时。");
        }

        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();
        return new CommandResult(process.ExitCode == 0, string.Join(Environment.NewLine, output, error).Trim());
    }

    private static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void TryWriteFailureResult(string[] args, string message)
    {
        try
        {
            if (args.Length >= 3)
            {
                WriteProbeResult(args[2], new NetworkProbeResult
                {
                    Success = false,
                    Message = message
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "写入网络探测失败结果失败。");
            // The parent process will report that no network configuration result was returned.
        }
    }

    private static void WriteProbeResult(string resultPath, NetworkProbeResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        File.WriteAllText(resultPath, JsonSerializer.Serialize(result));
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "终止网络探测辅助进程失败。");
            // If the elevated helper cannot be killed, return a timeout result to the UI.
        }
    }

    private static void TryDeleteResultFile(string resultPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(resultPath);
            string tempPath = Path.GetFullPath(Path.GetTempPath());
            if (fullPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "删除网络探测临时结果文件失败。");
            // Leaving a small temp result file is safer than deleting an unverified path.
        }
    }

    private readonly record struct CommandResult(bool Success, string Output);
}
