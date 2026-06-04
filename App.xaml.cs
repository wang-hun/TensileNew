using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TensileNeW.Models;
using TensileNeW.Services;
using HandyMessageBox = HandyControl.Controls.MessageBox;

namespace TensileNeW;

public partial class App : Application
{
    static App()
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssemblyFromLib;
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StartupWaitWindow? waitWindow = null;

        try
        {
            RAM.Init();
            ThemeManager.Apply(RAM.SettingModel.ColorSchemeName);
            Resources["SevenSegmentFontFamily"] = SevenSegmentFontHelper.DefaultFontFamily;
            DataAqc.InitVariables();

            bool isFirstRun = !SNModel.HasSnFile();
            bool needsToGenerateCache = ManualDocumentService.NeedsToGenerateCache();
            waitWindow = new StartupWaitWindow(
                needsToGenerateCache
                    ? "正在安装试验说明书，请稍后...."
                    : "正在加载安装试验说明书，请稍后....");
            waitWindow.SetHintVisibility(isFirstRun);
            waitWindow.Show();

            ManualDocumentStartupResult manualStartupResult = await Task.Run(ManualDocumentService.PrepareManualCache);
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            waitWindow.SetWaitText("正在连接控制器，请稍后....");
            Task<FontFamily> sevenSegmentFontTask = Task.Run(SevenSegmentFontHelper.GetFontFamilyOrDefault);
            Task minimumStartupDelayTask = Task.Delay(TimeSpan.FromSeconds(2));
            Task<bool> connectTask = TryConnectWithTimeoutAsync();
            bool connected = await connectTask;
            if (connected)
            {
                waitWindow.SetWaitText("GENBON");
                await Task.WhenAll(minimumStartupDelayTask, Task.Delay(TimeSpan.FromMilliseconds(500)));
            }
            else
            {
                await minimumStartupDelayTask;
            }

            Resources["SevenSegmentFontFamily"] = await sevenSegmentFontTask;

            MainWindow mainWindow = new(connected)
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

    private static async Task<bool> TryConnectWithTimeoutAsync()
    {
        try
        {
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
}
