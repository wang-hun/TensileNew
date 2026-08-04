using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using TensileNeW.Models;
using TensileNeW.Services;
using HandyMessageBox = HandyControl.Controls.MessageBox;

namespace TensileNeW;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\TensileNeW_ECS_SingleInstance";
    private const string SingleInstanceNoticeMutexName = @"Local\TensileNeW_ECS_SingleInstanceNotice";
    private static Mutex? singleInstanceMutex;

    static App()
    {
        AddPdfiumNativeSearchPath();
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssemblyFromLib;
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        if (NetworkAdapterProbeService.IsProbeWorker(e.Args))
        {
            Shutdown(NetworkAdapterProbeService.RunProbeWorker(e.Args));
            return;
        }

        if (!TryAcquireSingleInstanceMutex())
        {
            ShowSingleInstanceWindow();
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StartupWaitWindow? waitWindow = null;

        try
        {
            /*
             * ====================================================================
             * 程序启动时只读取一次试用状态，并保存到全局 RAM 配置。
             * ====================================================================
             */
            RAM.SetTrialPackageState(TrialPackageConfiguration.ReadStartupTrialState(AppContext.BaseDirectory));
            RAM.RecordTrialStartup();
            /*
             * ====================================================================
             * 一次性试用状态读取结束。
             * ====================================================================
             */
            RAM.Init();
            ThemeManager.Apply(RAM.SettingModel.ColorSchemeName);
            Resources["SevenSegmentFontFamily"] = SevenSegmentFontHelper.DefaultFontFamily;
            DataAqc.InitVariables();

            bool isFirstRun = !SNModel.HasSnFile();
            bool needsToGenerateCache = ManualDocumentService.NeedsToGenerateCache();
            waitWindow = new StartupWaitWindow(
                needsToGenerateCache
                    ? "正在安装试验说明书，请稍后..."
                    : "正在加载试验说明书，请稍后...");
            waitWindow.SetHintVisibility(isFirstRun);
            waitWindow.Show();

            ManualDocumentStartupResult manualStartupResult = await Task.Run(ManualDocumentService.PrepareManualCache);
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            waitWindow.SetWaitText("正在连接控制器，请稍后...");
            Task<FontFamily> sevenSegmentFontTask = Task.Run(SevenSegmentFontHelper.GetFontFamilyOrDefault);
            Task minimumStartupDelayTask = Task.Delay(TimeSpan.FromSeconds(2));
            Task<bool> connectTask = TryConnectWithTimeoutAsync();
            bool connected = await connectTask;

            Resources["SevenSegmentFontFamily"] = await sevenSegmentFontTask;

            await waitWindow.SetWaitTextAsync("正在扫描摄像头设备，请稍后...");
            CameraStartupResult cameraStartupResult = await Task.Run(ScanCameraDevicesAsync);

            await waitWindow.SetWaitTextAsync("GENBON");
            await Task.WhenAll(minimumStartupDelayTask, Task.Delay(TimeSpan.FromMilliseconds(800)));

            MainWindow mainWindow = new(connected, cameraStartupResult)
            {
                HasMissingManualOffice = manualStartupResult.HasMissingOffice
            };
            MainWindow = mainWindow;

            waitWindow.Close();
            waitWindow = null;

            mainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            waitWindow?.Close();
            HandyMessageBox.Error($"程序启动失败：{ex.Message}", "TensileNeW");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseSingleInstanceMutex();
        base.OnExit(e);
    }

    private static bool TryAcquireSingleInstanceMutex()
    {
        singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (createdNew)
        {
            return true;
        }

        try
        {
            if (singleInstanceMutex.WaitOne(0))
            {
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            return true;
        }

        singleInstanceMutex.Dispose();
        singleInstanceMutex = null;
        return false;
    }

    private static void ReleaseSingleInstanceMutex()
    {
        if (singleInstanceMutex is null)
        {
            return;
        }

        try
        {
            singleInstanceMutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        finally
        {
            singleInstanceMutex.Dispose();
            singleInstanceMutex = null;
        }
    }

    private void ShowSingleInstanceWindow()
    {
        using Mutex noticeMutex = new(true, SingleInstanceNoticeMutexName, out bool createdNew);
        if (!createdNew && !TryAcquireExistingMutex(noticeMutex))
        {
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            ThemeManager.Apply(GetConfiguredColorSchemeName());
        }
        catch
        {
            ThemeManager.Apply(ThemeManager.DefaultSchemeName);
        }

        new SingleInstanceWindow().ShowDialog();
    }

    private static bool TryAcquireExistingMutex(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static string GetConfiguredColorSchemeName()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, RAM.SettingFileName);
        if (!File.Exists(configPath) && File.Exists(RAM.SettingFileName))
        {
            configPath = RAM.SettingFileName;
        }

        if (!File.Exists(configPath))
        {
            return ThemeManager.DefaultSchemeName;
        }

        string? schemeName = JObject.Parse(File.ReadAllText(configPath))
            .Value<string>(nameof(SettingModel.ColorSchemeName));

        return string.IsNullOrWhiteSpace(schemeName)
            ? ThemeManager.DefaultSchemeName
            : schemeName;
    }

    private static async Task<bool> TryConnectWithTimeoutAsync()
    {
        try
        {
            bool hasSameSubnetAddress = await Task.Run(() =>
                NetworkAdapterProbeService.HasSameSubnetWiredAddress(RAM.SettingModel.PLC_IP));
            if (!hasSameSubnetAddress)
            {
                return false;
            }

            Task<bool> connectTask = Task.Run(() => DataAqc.TryConnect());
            Task completedTask = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(5)));

            if (completedTask == connectTask)
            {
                return await connectTask;
            }

            _ = connectTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<CameraStartupResult> ScanCameraDevicesAsync()
    {
        if (!CameraCaptureService.IsSupported)
        {
            return new CameraStartupResult([], null, null, "当前系统不支持摄像头采集。");
        }

        IReadOnlyList<CameraDeviceDescriptor> devices;
        try
        {
            devices = await CameraCaptureService.FindVideoCaptureDevicesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new CameraStartupResult([], null, null, $"摄像头扫描失败：{ex.Message}");
        }

        if (devices.Count == 0)
        {
            return new CameraStartupResult(devices, null, null, null);
        }

        string configuredId = RAM.SettingModel.CameraDeviceId;
        if (!string.IsNullOrWhiteSpace(configuredId))
        {
            bool configuredDeviceExists = devices.Any(device =>
                string.Equals(device.Id, configuredId, StringComparison.Ordinal));
            if (!configuredDeviceExists)
            {
                string name = string.IsNullOrWhiteSpace(RAM.SettingModel.CameraDeviceName)
                    ? configuredId
                    : RAM.SettingModel.CameraDeviceName;
                return new CameraStartupResult(devices, null, null, $"{name}摄像头连接失败");
            }
        }

        return new CameraStartupResult(devices, null, null, null);
    }
    private static Assembly? ResolveAssemblyFromLib(object? sender, ResolveEventArgs args)
    {
        string? assemblyName = new AssemblyName(args.Name).Name;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        string libPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", assemblyName + ".dll");
        return File.Exists(libPath) ? Assembly.LoadFrom(libPath) : null;
    }

    private static void AddPdfiumNativeSearchPath()
    {
        string x64Directory = Path.Combine(AppContext.BaseDirectory, "x64");
        if (Directory.Exists(x64Directory))
        {
            NativeLibrary.SetDllImportResolver(
                typeof(PdfiumViewer.PdfDocument).Assembly,
                (libraryName, assembly, searchPath) =>
                {
                    if (!string.Equals(libraryName, "pdfium.dll", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(libraryName, "pdfium", StringComparison.OrdinalIgnoreCase))
                    {
                        return IntPtr.Zero;
                    }

                    string pdfiumPath = Path.Combine(x64Directory, "pdfium.dll");
                    return NativeLibrary.TryLoad(pdfiumPath, out IntPtr handle) ? handle : IntPtr.Zero;
                });
        }
    }
}



