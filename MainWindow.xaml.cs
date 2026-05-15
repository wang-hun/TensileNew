using HandyControl.Data;
using NLog;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TensileNeW.Models;
using Dialog = HandyControl.Controls.Dialog;
using Growl = HandyControl.Controls.Growl;
using MessageBox = HandyControl.Controls.MessageBox;

namespace TensileNeW;

public partial class MainWindow : Window
{
    private const string GrowlToken = "MainGrowl";
    private const int SettingsUnlockClickCount = 6;
    private static GrowlInfo MakeInfo(string message) => new()
    {
        Message = message,
        WaitTime = 5,
        StaysOpen = false,
        ShowDateTime = false,
        IsCustom = true,
        Token = GrowlToken
    };

    private static void ShowInfo(string msg) => Growl.Info(msg, GrowlToken);
    private static void ShowSuccess(string msg) => Growl.Success(msg, GrowlToken);
    private static void ShowWarning(string msg) => Growl.Warning(MakeInfo(msg));
    private static void ShowError(string msg) => Growl.Error(MakeInfo(msg));

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly bool _connectedAtStartup;
    private readonly MainViewModel _viewModel;
    private VariableWindow? _variableWindow;
    private readonly List<double> _plotXs = [];
    private readonly List<double> _plotYs = [];
    private ScottPlot.Plottables.Scatter? _loadScatter;
    private int _plottedPointCount;
    private bool _plotInitialized;
    private int _logoClickCount;

    public static PLCVariable TimeVariable => FindVariable("拉伸时间");
    public static PLCVariable MaxForceVariable => FindVariable("最大拉伸力");
    public static PLCVariable RealDistanceVariable => FindVariable("实时拉伸位移");
    public static PLCVariable ValidDistanceVariable => FindVariable("有效拉伸位移");
    public static PLCVariable BeginForceVariable => FindVariable("主推力");
    public static PLCVariable PreSetForceVariable => FindVariable("冲程压边力设定");
    public static PLCVariable RealSpeedVariable => FindVariable("实时拉伸速度");
    public static PLCVariable RealForceVariable => FindVariable("实时拉伸力");
    public static PLCVariable RealPressVariable => FindVariable("实时压边力");
    public static PLCVariable ClosedLoopVariable => FindVariable("闭环压边力设定");
    public static PLCVariable ShutdownDelayVariable => FindVariable("停机延时设定");
    public static PLCVariable ShutdownRatioVariable => FindVariable("停机比例设定");
    public static PLCVariable SpeedVariable => FindVariable("速度设定");

    public static PLCVariable StartPressCoil => FindVariable("压边线圈");
    public static PLCVariable ReleasePressCoil => FindVariable("压边释放线圈");
    public static PLCVariable StartTensileCoil => FindVariable("拉伸线圈");
    public static PLCVariable ReleaseTensileCoil => FindVariable("拉伸释放线圈");
    public static PLCVariable TanliaoVariable => FindVariable("弹料");
    public static PLCVariable CalibrationStateVariable => FindVariable("传感器标零状态");

    public MainWindow(bool connectedAtStartup)
    {
        _connectedAtStartup = connectedAtStartup;
        _viewModel = new MainViewModel();
        _viewModel.RecipeWritten += name => Dispatcher.Invoke(() => ShowSuccess($"切换配方成功：{name}"));
        DataContext = _viewModel;
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Logger.Info("启动程序");
        InitializePlot();
        ChartHintPanel.Visibility = _viewModel.Setting.HideChartHintOnStartup
            ? Visibility.Collapsed
            : Visibility.Visible;
        DataAqc.LoadDataChanged += _ => Dispatcher.Invoke(RefreshPlot);
        DataAqc.ChartCleared += () => Dispatcher.Invoke(ResetPlot);
        DataAqc.Refresh(Dispatcher);
        DataAqc.StartConsumers(Dispatcher);

        if (!_connectedAtStartup)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ShowError("连接失败，请检查线路！");
                Dialog.Show(new ConnectionErrorDialog());
            });
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _variableWindow?.Close();
        _viewModel.SaveSettings();
        MainViewModel.StopConsumers();
        Logger.Info("关闭程序");
    }

    private void InitializePlot()
    {
        if (_plotInitialized)
        {
            return;
        }
        ApplyPlotLabels();
        LocalizePlotContextMenu();
        LoadPlot.Refresh();
        _plotInitialized = true;
    }

    private void ResetPlot()
    {
        LoadPlot.Plot.Clear();
        _plotXs.Clear();
        _plotYs.Clear();
        _loadScatter = null;
        _plottedPointCount = 0;
        ApplyPlotLabels();
        LoadPlot.Refresh();
    }

    private void RefreshPlot()
    {
        if (!_plotInitialized)
        {
            InitializePlot();
        }

        var items = DataAqc.loadModels;
        if (items == null)
        {
            return;
        }

        if (items.Count < _plottedPointCount)
        {
            ResetPlot();
        }

        while (_plottedPointCount < items.Count)
        {
            var item = items[_plottedPointCount];
            _plotXs.Add(item.RealDistance);
            _plotYs.Add(item.RealForce);
            _plottedPointCount++;
        }

        if (_loadScatter == null && _plotXs.Count > 0)
        {
            _loadScatter = LoadPlot.Plot.Add.Scatter(_plotXs, _plotYs);
            _loadScatter.Smooth = true;
        }

        ApplyPlotLabels();
        LoadPlot.Refresh();
    }

    private void ApplyPlotLabels()
    {
        LoadPlot.Plot.Title("载荷位移数据", 30);
        LoadPlot.Plot.XLabel("位移", 30);
        LoadPlot.Plot.YLabel("载荷", 30);
        LoadPlot.Plot.Axes.Bottom.TickLabelStyle.FontSize = 22;
        LoadPlot.Plot.Axes.Left.TickLabelStyle.FontSize = 22;
        LoadPlot.Plot.Axes.Bottom.TickGenerator.MaxTickCount = 6;
        LoadPlot.Plot.Axes.Left.TickGenerator.MaxTickCount = 6;
        LoadPlot.Plot.Font.Automatic();
    }

    private void LocalizePlotContextMenu()
    {
        if (string.Equals(RAM.SettingModel.Language, "EN", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var menu = LoadPlot.Menu;
        if (menu == null) return;

        var items = menu.ContextMenuItems;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            switch (item.Label)
            {
                case "Save Image":
                    item.Label = "保存图片";
                    items[i] = item;
                    break;
                case "Copy to Clipboard":
                    item.Label = "复制到剪贴板";
                    items[i] = item;
                    break;
                case "Autoscale":
                    item.Label = "自动缩放";
                    items[i] = item;
                    break;
                case "Open in New Window":
                    item.Label = "新窗口打开";
                    items[i] = item;
                    break;
            }
        }
    }

    private void Home_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Home";
    private void Settings_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Settings";
    private void Variables_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Variables";

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        ReconnectButton.IsEnabled = false;
        IsEnabled = false;

        var waitWindow = new StartupWaitWindow(GetConnectWaitText())
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        try
        {
            waitWindow.Show();
            var reconnectTask = TryReconnectWithTimeoutAsync();
            await Task.WhenAll(reconnectTask, Task.Delay(TimeSpan.FromSeconds(2)));
            bool connected = await reconnectTask;

            if (!connected)
            {
                ShowError("\u8fde\u63a5\u5931\u8d25\uff0c\u8bf7\u68c0\u67e5\u7ebf\u8def\uff01");
                Dialog.Show(new ConnectionErrorDialog());
            }
        }
        finally
        {
            waitWindow.Close();
            IsEnabled = true;
            ReconnectButton.IsEnabled = true;
        }
    }

    private static string GetConnectWaitText()
    {
        return string.Equals(RAM.SettingModel.Language, "EN", StringComparison.OrdinalIgnoreCase)
            ? "Connecting to PLC controller, please wait..."
            : "\u8fde\u63a5PLC\u63a7\u5236\u5668\u4e2d\uff0c\u8bf7\u7a0d\u540e...";
    }

    private static async Task<bool> TryReconnectWithTimeoutAsync()
    {
        try
        {
            var reconnectTask = Task.Run(() => DataAqc.TryReconnect(forceReconnect: true));
            var completedTask = await Task.WhenAny(reconnectTask, Task.Delay(TimeSpan.FromSeconds(5)));

            if (completedTask == reconnectTask)
            {
                return await reconnectTask;
            }

            _ = reconnectTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void ChartHintButton_Click(object sender, RoutedEventArgs e)
    {
        ChartHintPanel.Visibility = ChartHintPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ChartHintStartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveSettings();
    }

    private void LogoImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _logoClickCount++;
        if (_logoClickCount < SettingsUnlockClickCount)
        {
            return;
        }

        _logoClickCount = 0;
        if (VariablesButton.Visibility == Visibility.Visible)
        {
            VariablesButton.Visibility = Visibility.Hidden;
            if (_viewModel.CurrentPage == "Variables")
            {
                _viewModel.CurrentPage = "Home";
            }
            return;
        }

        var dialog = new SettingsPinDialog();
        dialog.Unlocked += (_, _) =>
        {
            VariablesButton.Visibility = Visibility.Visible;
        };
        Dialog.Show(dialog);
    }

    private async void StartPress_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("压边");
    private async void ReleasePress_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("压边释放");
    private async void StartTensile_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("拉伸");
    private async void ReleaseTensile_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("拉伸释放");
    private async void Stop_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("停止");
    private async void Reset_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("数据重置");
    private async void Calibration_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("传感器标零");
    private async void WriteRecipe_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await _viewModel.WriteRecipeAsync();
        if (ok)
        {
            ShowSuccess("写入配方参数完成");
        }
        else
        {
            ShowError("写入配方参数失败，请检查连接");
        }
    }
    private async void ClosePress_Down(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("冲程压边", true);
    private async void ClosePress_Up(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("冲程压边", false);
    private async void Tanliao_Down(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("弹料", true);
    private async void Tanliao_Up(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("弹料", false);

    private void SaveData_Click(object sender, RoutedEventArgs e) => _viewModel.SaveDataAs();
    private void AddRecipe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RecipeNameDialog();
        dialog.Confirmed += (_, name) =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowWarning("配方名称不能为空");
                return;
            }

            if (!_viewModel.AddRecipe(name))
            {
                ShowWarning("配方名称已经存在，请修改");
                return;
            }

            ShowSuccess("添加试验成功");
        };
        Dialog.Show(dialog);
    }

    private void DeleteRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedRecipe == null)
        {
            ShowWarning("请先选择试验");
            return;
        }

        var dialog = new RecipeConfirmDialog();
        dialog.Confirmed += (_, _) =>
        {
            _viewModel.DeleteRecipe();
            ShowSuccess("删除试验成功");
        };
        Dialog.Show(dialog);
    }
    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveSettingsAndApplyLanguage();
        ShowSuccess("保存成功");
    }

    private void BrowseExcelFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择Excel保存路径"
        };
        if (System.IO.Directory.Exists(_viewModel.Setting.ExcelFolderPath))
        {
            dialog.InitialDirectory = _viewModel.Setting.ExcelFolderPath;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _viewModel.Setting.ExcelFolderPath = dialog.FolderName;
    }

    private void OpenVariablesWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_variableWindow is { IsVisible: true })
        {
            _variableWindow.Activate();
            return;
        }

        _variableWindow = new VariableWindow
        {
            Owner = this,
            DataContext = _viewModel
        };
        _variableWindow.Closed += (_, _) => _variableWindow = null;
        _variableWindow.Show();
    }

    private void ShutdownRatioBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        string dec = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

        foreach (char ch in e.Text)
        {
            if (char.IsControl(ch)) continue;
            if (!char.IsDigit(ch) && ch.ToString() != dec)
            {
                e.Handled = true;
                return;
            }
        }

        string before = tb.Text.Substring(0, tb.SelectionStart);
        string after = tb.Text.Substring(tb.SelectionStart + tb.SelectionLength);
        string proposed = before + e.Text + after;

        if (proposed == dec) return;
        if (proposed.EndsWith(dec)) return;

        if (decimal.TryParse(proposed, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out decimal val))
        {
            if (val < 0m || val > 1m)
            {
                e.Handled = true;
            }
        }
    }

    private void DecimalBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        string dec = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

        foreach (char ch in e.Text)
        {
            if (char.IsControl(ch)) continue;
            if (!char.IsDigit(ch) && ch.ToString() != dec)
            {
                e.Handled = true;
                return;
            }
        }

        string before = tb.Text.Substring(0, tb.SelectionStart);
        string after = tb.Text.Substring(tb.SelectionStart + tb.SelectionLength);
        string proposed = before + e.Text + after;

        if (proposed == dec) return;
        if (proposed.EndsWith(dec)) return;

        if (!decimal.TryParse(proposed, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out _))
        {
            e.Handled = true;
        }
    }

    private void ShutdownRatioBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        if (string.IsNullOrWhiteSpace(tb.Text)) return;

        string dec = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (tb.Text == dec)
        {
            tb.Text = "0" + dec;
            return;
        }

        if (decimal.TryParse(tb.Text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out decimal val))
        {
            if (val < 0m) val = 0m;
            if (val > 1m) val = 1m;
            tb.Text = val.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
        else
        {
            tb.Text = "0";
        }
    }

    private static PLCVariable FindVariable(string name)
    {
        DataAqc.EnsureInitialized();
        return DataAqc.PLCVariables.First(t => t.Name == name);
    }
}
