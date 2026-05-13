using System;
using System.Threading.Tasks;
using System.Windows;
using TensileNeW.Models;
using HandyMessageBox = HandyControl.Controls.MessageBox;

namespace TensileNeW;

public partial class App : Application
{
    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StartupWaitWindow? waitWindow = null;

        try
        {
            RAM.Init();
            DataAqc.InitVariables();

            bool isEn = string.Equals(RAM.SettingModel.Language, "EN", StringComparison.OrdinalIgnoreCase);
            string waitText = isEn
                ? "Connecting to PLC controller, please wait..."
                : "连接PLC控制器中，请稍后...";

            waitWindow = new StartupWaitWindow(waitText);
            waitWindow.Show();

            var connectTask = TryConnectWithTimeoutAsync();
            await Task.WhenAll(connectTask, Task.Delay(TimeSpan.FromSeconds(2)));
            bool connected = await connectTask;

            var mainWindow = new MainWindow(connected);
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
            var connectTask = Task.Run(() => DataAqc.TryConnect());
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(5)));

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
}
