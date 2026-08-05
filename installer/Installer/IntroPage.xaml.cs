using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace EcsInstaller;

public sealed partial class IntroPage : UserControl
{
    public event EventHandler<InstallRequestEventArgs>? InstallRequested;

    public IntroPage()
    {
        InitializeComponent();
        VersionTextBlock.Text = GetDisplayVersion();
        TrialShield.Visibility = InstallerService.IsTrialPackage
            ? Visibility.Visible
            : Visibility.Collapsed;
        InstallPathTextBox.Text = InstallerService.GetDefaultInstallPath();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using Forms.FolderBrowserDialog dialog = new()
        {
            Description = "选择 ECS 部署路径",
            UseDescriptionForTitle = true,
            SelectedPath = InstallPathTextBox.Text
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            InstallPathTextBox.Text = InstallerService.AppendPackageDirectory(dialog.SelectedPath);
        }
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        string installPath = InstallPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(installPath))
        {
            MessageBox.Show(Window.GetWindow(this), "请选择部署路径。", "ECS Installer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        InstallRequested?.Invoke(this, new InstallRequestEventArgs(installPath, DesktopShortcutCheckBox.IsChecked == true));
    }

    private static string GetDisplayVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(version) ? "安装程序" : $"安装程序 {version}";
    }
}

public sealed class InstallRequestEventArgs(string installPath, bool createDesktopShortcut) : EventArgs
{
    public string InstallPath { get; } = installPath;

    public bool CreateDesktopShortcut { get; } = createDesktopShortcut;
}
