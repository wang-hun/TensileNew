using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

public static class NetworkAdapterProbeService
{
    private const string ProbeArg = "--network-probe";
    private const int ModbusTcpPort = 502;
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static bool IsProbeWorker(string[] args) =>
        args.Length >= 3 && string.Equals(args[0], ProbeArg, StringComparison.OrdinalIgnoreCase);

    public static int RunProbeWorker(string[] args)
    {
        try
        {
            string plcIp = args[1];
            string resultPath = args[2];
            NetworkProbeResult result = Task.Run(() => ProbeAllWiredAdaptersAsync(plcIp)).GetAwaiter().GetResult();
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(result));
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
            targetIp.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        return EnumerateWiredAdapters()
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Any(address => IsInSameSubnet(address.Address, targetIp, address.PrefixLength));
    }

    public static async Task<NetworkProbeResult> RunElevatedProbeAsync(string plcIp)
    {
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            return new NetworkProbeResult
            {
                Success = false,
                Message = "无法找到当前程序路径，不能启动网络探测。"
            };
        }

        string resultPath = Path.Combine(Path.GetTempPath(), $"TensileNeW-NetworkProbe-{Guid.NewGuid():N}.json");
        try
        {
            using Process process = StartElevatedProbeProcess(processPath, plcIp, resultPath);
            Task exitTask = process.WaitForExitAsync();
            Task completedTask = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(45)));
            if (completedTask != exitTask)
            {
                TryKillProcess(process);
                return new NetworkProbeResult
                {
                    Success = false,
                    Message = "网络探测超时，请检查设备线路后重试。"
                };
            }

            if (!File.Exists(resultPath))
            {
                return new NetworkProbeResult
                {
                    Success = false,
                    Message = "网络探测未返回结果。"
                };
            }

            string json = await File.ReadAllTextAsync(resultPath);
            NetworkProbeResult? result = JsonSerializer.Deserialize<NetworkProbeResult>(json);
            return result ?? new NetworkProbeResult
            {
                Success = false,
                Message = "网络探测结果无效。"
            };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new NetworkProbeResult
            {
                Success = false,
                Message = "已取消管理员权限请求。"
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "启动网络探测失败。");
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

    private static Process StartElevatedProbeProcess(string processPath, string plcIp, string resultPath)
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
        startInfo.ArgumentList.Add(plcIp);
        startInfo.ArgumentList.Add(resultPath);

        return Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动网络探测进程。");
    }

    private static async Task<NetworkProbeResult> ProbeAllWiredAdaptersAsync(string plcIp)
    {
        if (!IPAddress.TryParse(plcIp, out IPAddress? targetIp) ||
            targetIp.AddressFamily != AddressFamily.InterNetwork)
        {
            return new NetworkProbeResult
            {
                Success = false,
                Message = $"设备 IP 无效：{plcIp}"
            };
        }

        List<NetworkInterface> adapters = EnumerateWiredAdapters().ToList();
        if (adapters.Count == 0)
        {
            return new NetworkProbeResult
            {
                Success = false,
                Message = "未发现已连接的有线网卡。"
            };
        }

        HashSet<IPAddress> existingAddresses = GetAllLocalIPv4Addresses();
        foreach (NetworkInterface adapter in adapters)
        {
            foreach (IPAddress localIp in BuildCandidateLocalIps(targetIp, existingAddresses))
            {
                bool added = false;
                bool keepAddress = false;
                try
                {
                    CommandResult addResult = RunNetsh("interface", "ipv4", "add", "address",
                        $"name={adapter.Name}",
                        $"address={localIp}",
                        "mask=255.255.255.0");

                    if (!addResult.Success)
                    {
                        Logger.Warn($"添加额外 IP 失败：{adapter.Name} {localIp} {addResult.Output}");
                        continue;
                    }

                    added = true;
                    existingAddresses.Add(localIp);
                    await Task.Delay(700);

                    if (await CanDetectDeviceAsync(localIp, targetIp))
                    {
                        keepAddress = true;
                        return new NetworkProbeResult
                        {
                            Success = true,
                            AdapterName = adapter.Name,
                            AdapterDescription = adapter.Description,
                            LocalIp = localIp.ToString(),
                            Message = $"已在 {adapter.Name} 上找到设备。"
                        };
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"探测网卡失败：{adapter.Name}");
                }
                finally
                {
                    if (added && !keepAddress)
                    {
                        RemoveAddress(adapter.Name, localIp);
                    }
                }
            }
        }

        return new NetworkProbeResult
        {
            Success = false,
            Message = "所有有线网卡均未探测到设备。"
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

    private static async Task<bool> CanDetectDeviceAsync(IPAddress localIp, IPAddress targetIp)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(500);
            }

            if (await CanConnectFromLocalIpAsync(localIp, targetIp))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> CanConnectFromLocalIpAsync(IPAddress localIp, IPAddress targetIp)
    {
        try
        {
            using TcpClient client = new(new IPEndPoint(localIp, 0));
            Task connectTask = client.ConnectAsync(targetIp, ModbusTcpPort);
            Task completedTask = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(2)));
            if (completedTask != connectTask)
            {
                return false;
            }

            await connectTask;
            return client.Connected;
        }
        catch
        {
            return false;
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
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address)
            .ToHashSet();
    }

    private static void RemoveAddress(string adapterName, IPAddress localIp)
    {
        CommandResult removeResult = RunNetsh("interface", "ipv4", "delete", "address",
            $"name={adapterName}",
            $"address={localIp}");

        if (!removeResult.Success)
        {
            Logger.Warn($"移除额外 IP 失败：{adapterName} {localIp} {removeResult.Output}");
        }
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
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

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
                File.WriteAllText(args[2], JsonSerializer.Serialize(new NetworkProbeResult
                {
                    Success = false,
                    Message = message
                }));
            }
        }
        catch
        {
            // The parent process will report that no probe result was returned.
        }
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
        catch
        {
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
        catch
        {
            // Leaving a small temp result file is safer than deleting an unverified path.
        }
    }

    private readonly record struct CommandResult(bool Success, string Output);
}
