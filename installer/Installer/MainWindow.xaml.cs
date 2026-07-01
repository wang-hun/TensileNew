using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace EcsInstaller;

public partial class MainWindow : Window
{
    private string? _installedExePath;
    private DeployingPage? _deployingPage;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        ShowIntroPage();
    }

    private void ShowIntroPage()
    {
        IntroPage page = new();
        page.InstallRequested += IntroPage_InstallRequested;
        PageHost.Content = page;
    }

    private async void IntroPage_InstallRequested(object? sender, InstallRequestEventArgs e)
    {
        _deployingPage = new DeployingPage();
        PageHost.Content = _deployingPage;

        try
        {
            InstallerOptions options = new(e.InstallPath, e.CreateDesktopShortcut);
            _installedExePath = await Task.Run(() => InstallerService.Install(options, SetProgress));
            ShowDonePage();
        }
        catch (Exception ex)
        {
            ShowIntroPage();
            MessageBox.Show(this, "部署失败：" + ex.Message, "ECS Installer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowDonePage()
    {
        DonePage page = new();
        page.CloseRequested += (_, _) =>
        {
            _allowClose = true;
            Close();
        };
        page.LaunchRequested += (_, _) => LaunchInstalledApp();
        PageHost.Content = page;
    }

    private void LaunchInstalledApp()
    {
        if (!string.IsNullOrWhiteSpace(_installedExePath) && File.Exists(_installedExePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _installedExePath,
                WorkingDirectory = Path.GetDirectoryName(_installedExePath),
                UseShellExecute = true
            });
        }

        _allowClose = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmClose())
        {
            _allowClose = true;
            Close();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (ConfirmClose())
        {
            _allowClose = true;
            return;
        }

        e.Cancel = true;
    }

    private bool ConfirmClose()
    {
        return MessageBox.Show(
            this,
            "确认关闭安装器？",
            "ECS Installer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void WindowDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void SetProgress(string text)
    {
        Dispatcher.Invoke(() => _deployingPage?.SetProgress(text));
    }
}
