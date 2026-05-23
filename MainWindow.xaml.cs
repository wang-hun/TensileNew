using HandyControl.Data;
using NLog;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
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
    private const bool AutoScrollXAxisEnabled = false;
    private const double HelpZoomStep = 0.1;
    private const double HelpMinZoom = 0.5;
    private const double HelpMaxZoom = 2.0;
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
    private LoadDataWindow? _loadDataWindow;
    private PlotWindow? _plotWindow;
    private readonly List<double> _plotXs = [];
    private readonly List<double> _plotYs = [];
    private ScottPlot.Plottables.Scatter? _loadScatter;
    private int _plottedPointCount;
    private bool _plotInitialized;
    private int _logoClickCount;
    private bool _autoTrackLatestPoint = true;
    private double _helpZoomFactor = 1.0;
    private Uri? _helpDocumentUri;
    private readonly System.Windows.Threading.DispatcherTimer _loadScrollTimer;
    private readonly System.Windows.Threading.DispatcherTimer _plotAutoscaleTimer;
    private int _pendingLoadScrollIndex = -1;
    private static readonly ScottPlot.Color SanaePlotBackgroundColor = ScottPlot.Color.FromHex("#E1E2DC");
    private static readonly ScottPlot.Color SanaePlotLineColor = ScottPlot.Color.FromHex("#101010");
    private static readonly ScottPlot.Color SanaePlotGridLineColor = ScottPlot.Color.FromHex("#000000");
    private ScottPlot.Color _defaultPlotBackgroundColor;
    private ScottPlot.Color _defaultPlotMajorGridLineColor;
    private ScottPlot.Color? _defaultLoadScatterColor;

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
        _defaultPlotBackgroundColor = LoadPlot.Plot.DataBackground.Color;
        _defaultPlotMajorGridLineColor = LoadPlot.Plot.Grid.MajorLineColor;
        _loadScrollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _loadScrollTimer.Tick += (_, _) =>
        {
            _loadScrollTimer.Stop();
            ScrollMainLoadDataToLatest();
        };
        _plotAutoscaleTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _plotAutoscaleTimer.Tick += (_, _) => AutoScalePlotWhileCollecting();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeTitleBarHelper.ApplyTheme(this);
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
        _viewModel.LoadItems.ListChanged += LoadItems_ListChanged;
        DataAqc.Refresh(Dispatcher);
        DataAqc.StartConsumers(Dispatcher);
        _plotAutoscaleTimer.Start();
        LoadHelpDocument();

        if (!_connectedAtStartup)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ShowError("连接失败，请检查线路！");
                Dialog.Show(new ConnectionErrorDialog());
            });
        }
    }

    private void LoadHelpDocument()
    {
        try
        {
            _helpDocumentUri = HelpDocumentLoader.TryGetDefaultDocumentUri();
            if (_helpDocumentUri is not null)
            {
                HelpWebView.Source = _helpDocumentUri;
            }

            HelpNavigationTree.ItemsSource = HelpDocumentLoader.LoadNavigation();
        }
        catch
        {
            _helpDocumentUri = null;
            HelpWebView.Source = null;
            HelpNavigationTree.ItemsSource = null;
        }
    }

    private async void HelpNavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        await NavigateHelpItemAsync(e.NewValue as HelpNavigationItem);
    }

    private async void HelpNavigationTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HelpNavigationTree.SelectedItem is not HelpNavigationItem item)
        {
            return;
        }

        await NavigateHelpItemAsync(item);
        e.Handled = true;
    }

    private async Task NavigateHelpItemAsync(HelpNavigationItem? item)
    {
        try
        {
            Uri? targetUri = HelpDocumentLoader.TryBuildNavigationUri(item);
            if (targetUri is null)
            {
                return;
            }

            UriBuilder documentBuilder = new(targetUri)
            {
                Fragment = string.Empty
            };
            Uri documentUri = documentBuilder.Uri;
            bool shouldLoadDocument = HelpWebView.Source is null ||
                !string.Equals(HelpWebView.Source.LocalPath, documentUri.LocalPath, StringComparison.OrdinalIgnoreCase);

            if (shouldLoadDocument)
            {
                TaskCompletionSource navigationCompleted = new();

                void Handler(object? _, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs __)
                {
                    HelpWebView.NavigationCompleted -= Handler;
                    navigationCompleted.TrySetResult();
                }

                HelpWebView.NavigationCompleted += Handler;
                HelpWebView.Source = documentUri;
                await navigationCompleted.Task;
            }

            await HelpWebView.EnsureCoreWebView2Async();
            if (string.IsNullOrWhiteSpace(item?.Anchor))
            {
                await HelpWebView.ExecuteScriptAsync("window.scrollTo({ top: 0, behavior: 'smooth' });");
                return;
            }

            string anchor = JsonSerializer.Serialize(item.Anchor);
            await HelpWebView.ExecuteScriptAsync($$"""
                (() => {
                    const anchor = {{anchor}};
                    const target = document.getElementById(anchor) || document.getElementsByName(anchor)[0];
                    if (!target) {
                        return false;
                    }

                    target.scrollIntoView({ block: 'start', behavior: 'smooth' });
                    return true;
                })();
                """);
        }
        catch
        {
            // Keep the help page unchanged if navigation fails.
        }
    }

    private async Task ScrollHelpDocumentToTopAsync()
    {
        try
        {
            if (_helpDocumentUri is null)
            {
                return;
            }

            if (HelpWebView.Source is null || HelpWebView.Source.LocalPath != _helpDocumentUri.LocalPath)
            {
                HelpWebView.Source = _helpDocumentUri;
            }

            await HelpWebView.EnsureCoreWebView2Async();
            await HelpWebView.ExecuteScriptAsync("window.scrollTo({ top: 0, behavior: 'smooth' });");
        }
        catch
        {
            // Keep the help page unchanged if WebView2 is not available.
        }
    }

    private void HelpZoomOut_Click(object sender, RoutedEventArgs e)
    {
        SetHelpZoom(_helpZoomFactor - HelpZoomStep);
    }

    private void HelpZoomIn_Click(object sender, RoutedEventArgs e)
    {
        SetHelpZoom(_helpZoomFactor + HelpZoomStep);
    }

    private void HelpZoomReset_Click(object sender, RoutedEventArgs e)
    {
        SetHelpZoom(1.0);
    }

    private void HelpZoomBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyHelpZoomBoxValue();
    }

    private void HelpZoomBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyHelpZoomBoxValue();
            HelpZoomBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ApplyHelpZoomBoxValue()
    {
        string zoomText = HelpZoomBox.Text.Trim().TrimEnd('%');
        if (double.TryParse(zoomText, out double zoomPercent))
        {
            SetHelpZoom(zoomPercent / 100.0);
            return;
        }

        UpdateHelpZoomText();
    }

    private void SetHelpZoom(double zoomFactor)
    {
        _helpZoomFactor = Math.Clamp(zoomFactor, HelpMinZoom, HelpMaxZoom);
        UpdateHelpZoomText();

        try
        {
            HelpWebView.ZoomFactor = _helpZoomFactor;
        }
        catch
        {
            // WebView2 may not be initialized or available. Keep the help page blank/unchanged.
        }
    }

    private void UpdateHelpZoomText()
    {
        HelpZoomBox.Text = $"{_helpZoomFactor:P0}";
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _variableWindow?.Close();
        _loadDataWindow?.Close();
        _plotWindow?.Close();
        _viewModel.LoadItems.ListChanged -= LoadItems_ListChanged;
        _plotAutoscaleTimer.Stop();
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

    private void AutoScalePlotWhileCollecting()
    {
        if (!_autoTrackLatestPoint || !IsDataCollecting() || _loadScatter == null || _plotXs.Count == 0)
        {
            return;
        }

        LoadPlot.Plot.Axes.AutoScale();
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

        var limits = LoadPlot.Plot.Axes.GetLimits();
        if (items.Count < _plottedPointCount)
        {
            ResetPlot();
            limits = LoadPlot.Plot.Axes.GetLimits();
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
            _defaultLoadScatterColor = _loadScatter.Color;
        }

        ApplyPlotLabels();
        if (AutoScrollXAxisEnabled && _autoTrackLatestPoint && _plotXs.Count > 0)
        {
            double xSpan = limits.Right - limits.Left;
            if (xSpan <= 0)
            {
                xSpan = Math.Max(1, _plotXs.Max() - _plotXs.Min());
            }

            double latestX = _plotXs[^1];
            LoadPlot.Plot.Axes.SetLimits(latestX - xSpan, latestX, limits.Bottom, limits.Top);
        }
        LoadPlot.Refresh();
    }

    private void ApplyPlotLabels()
    {
        ApplyPlotStyle();
        LoadPlot.Plot.Title("力位移数据", 30);
        LoadPlot.Plot.XLabel("位移（mm）", 30);
        LoadPlot.Plot.YLabel("力（KN）", 30);
        LoadPlot.Plot.Axes.Bottom.TickLabelStyle.FontSize = 22;
        LoadPlot.Plot.Axes.Left.TickLabelStyle.FontSize = 22;
        LoadPlot.Plot.Axes.Bottom.TickGenerator.MaxTickCount = 6;
        LoadPlot.Plot.Axes.Left.TickGenerator.MaxTickCount = 6;
        LoadPlot.Plot.Font.Automatic();
    }

    private void ApplyPlotStyle()
    {
        bool useSanae = string.Equals(ThemeManager.CurrentScheme.Name, ThemeManager.DefaultSchemeName, StringComparison.Ordinal);

        LoadPlot.Plot.DataBackground.Color = useSanae ? SanaePlotBackgroundColor : _defaultPlotBackgroundColor;
        LoadPlot.Plot.Grid.MajorLineColor = useSanae ? SanaePlotGridLineColor : _defaultPlotMajorGridLineColor;
        LoadPlot.Plot.Grid.MinorLineColor = useSanae ? SanaePlotGridLineColor : _defaultPlotMajorGridLineColor;
        if (_loadScatter != null)
        {
            _loadScatter.Color = useSanae
                ? SanaePlotLineColor
                : _defaultLoadScatterColor ?? _loadScatter.Color;
        }
    }

    private void LocalizePlotContextMenu()
    {
        var menu = LoadPlot.Menu;
        if (menu == null) return;

        bool isEn = string.Equals(RAM.SettingModel.Language, "EN", StringComparison.OrdinalIgnoreCase);
        var items = menu.ContextMenuItems;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            switch (item.Label)
            {
                case "Save Image":
                    if (!isEn)
                    {
                        item.Label = "保存图片";
                    }
                    items[i] = item;
                    break;
                case "Copy to Clipboard":
                    if (!isEn)
                    {
                        item.Label = "复制到剪贴板";
                    }
                    items[i] = item;
                    break;
                case "Autoscale":
                    if (!isEn)
                    {
                        item.Label = "自动缩放";
                    }
                    items[i] = item;
                    break;
                case "Open in New Window":
                    if (!isEn)
                    {
                        item.Label = "新窗口打开";
                    }
                    item.OnInvoke = _ => OpenPlotWindow();
                    items[i] = item;
                    break;
            }
        }
    }

    private void OpenPlotWindow()
    {
        if (_plotWindow is { IsVisible: true })
        {
            _plotWindow.Activate();
            return;
        }

        _plotWindow = new PlotWindow(() => _autoTrackLatestPoint)
        {
            Owner = this
        };
        _plotWindow.Closed += (_, _) => _plotWindow = null;
        _plotWindow.Show();
    }

    private void Home_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Home";
    private void Settings_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Settings";
    private void Help_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Help";
    private void Variables_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Variables";
    private void ColorSchemes_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "ColorSchemes";

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        ReconnectButton.IsEnabled = false;
        IsEnabled = false;

        var waitWindow = new StartupWaitWindow(GetConnectWaitText());

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
            ? "Connecting to device host, please wait..."
            : "正在连接 设备主机，请稍后...";
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

    private void AutoTrackLatestPointCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _autoTrackLatestPoint = AutoTrackLatestPointCheckBox.IsChecked == true;
    }

    private void LoadItems_ListChanged(object? sender, ListChangedEventArgs e)
    {
        if (MainLoadDataAutoScrollCheckBox.IsChecked != true ||
            e.ListChangedType != ListChangedType.ItemAdded ||
            e.NewIndex < 0)
        {
            return;
        }

        _pendingLoadScrollIndex = e.NewIndex;
        if (!_loadScrollTimer.IsEnabled)
        {
            _loadScrollTimer.Start();
        }
    }

    private void ScrollMainLoadDataToLatest()
    {
        if (_pendingLoadScrollIndex < 0 || _pendingLoadScrollIndex >= _viewModel.LoadItems.Count)
        {
            return;
        }

        MainLoadDataGrid.ScrollIntoView(_viewModel.LoadItems[_pendingLoadScrollIndex]);
        _pendingLoadScrollIndex = -1;
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
        dialog.ColorUnlocked += (_, _) =>
        {
            ColorSchemesButton.Visibility = Visibility.Visible;
            _viewModel.CurrentPage = "ColorSchemes";
        };
        Dialog.Show(dialog);
    }

    private void ApplyColorScheme_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ColorScheme scheme)
        {
            return;
        }

        ThemeManager.Apply(scheme);
        _viewModel.Setting.ColorSchemeName = scheme.Name;
        _viewModel.SaveSettings();
        NativeTitleBarHelper.ApplyTheme(this);
        ApplyPlotLabels();
        LoadPlot.Refresh();
        _plotWindow?.ApplyCurrentTheme();
        ShowSuccess($"已应用配色方案：{scheme.Name}");
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
        if (!_viewModel.IsSelectedRecipeEditable)
        {
            ShowWarning("默认配置 不允许修改及删除。");
            return;
        }

        bool ok = await _viewModel.WriteRecipeAsync();
        if (ok)
        {
            string name = _viewModel.SelectedRecipe?.RecipeName ?? string.Empty;
            ShowSuccess($"已写入配置参数：{name}");
        }
        else
        {
            ShowError("写入配置参数失败，请检查连接");
        }
    }
    private async void ClosePress_Down(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("冲程压边", true);
    private async void ClosePress_Up(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("冲程压边", false);
    private async void Tanliao_Down(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("弹料", true);
    private async void Tanliao_Up(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("弹料", false);

    private void SaveData_Click(object sender, RoutedEventArgs e) => _viewModel.SaveDataAs();

    private void OpenLoadDataWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_loadDataWindow is { IsVisible: true })
        {
            _loadDataWindow.Activate();
            return;
        }

        _loadDataWindow = new LoadDataWindow
        {
            Owner = this,
            DataContext = _viewModel
        };
        _loadDataWindow.Closed += (_, _) => _loadDataWindow = null;
        _loadDataWindow.Show();
    }

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

        if (!_viewModel.IsSelectedRecipeEditable)
        {
            ShowWarning("默认配置 不允许修改及删除。");
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

    private void DecimalBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        if (TryNormalizeDecimalInput(tb.Text, out string normalized))
        {
            ClearInputValidationState(tb);
            tb.Text = normalized;
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        }
        else
        {
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            ClearInputValidationState(tb);
        }
    }

    private void DecimalBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        SetInputValidationState(tb, TryNormalizeDecimalInput(tb.Text, out _), "请输入数字。");
    }

    private void ShutdownRatioBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        if (!TryNormalizeDecimalInput(tb.Text, out string normalized))
        {
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            ClearInputValidationState(tb);
            return;
        }

        if (!decimal.TryParse(normalized, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out decimal value))
        {
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            ClearInputValidationState(tb);
            return;
        }

        if (value < 0m || value > 1m)
        {
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            ClearInputValidationState(tb);
            return;
        }

        ClearInputValidationState(tb);
        tb.Text = normalized;
        tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
    }

    private void ShutdownRatioBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        if (!TryNormalizeDecimalInput(tb.Text, out string normalized))
        {
            SetInputValidationState(tb, false, "请输入数字，范围为 0 到 1。");
            return;
        }

        if (!decimal.TryParse(normalized, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out decimal value) || value < 0m || value > 1m)
        {
            SetInputValidationState(tb, false, "停机比例必须在 0 到 1 之间。");
            return;
        }

        ClearInputValidationState(tb);
    }

    private static void SetInputValidationState(System.Windows.Controls.TextBox tb, bool isValid, string message)
    {
        if (isValid)
        {
            ClearInputValidationState(tb);
            return;
        }

        tb.BorderBrush = System.Windows.Media.Brushes.Red;
        tb.BorderThickness = new Thickness(1.5);
        tb.ToolTip = message;
    }

    private static void ClearInputValidationState(System.Windows.Controls.TextBox tb)
    {
        tb.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        tb.ClearValue(System.Windows.Controls.Control.BorderThicknessProperty);
        tb.ClearValue(FrameworkElement.ToolTipProperty);
    }

    private static bool TryNormalizeDecimalInput(string text, out string normalized)
    {
        string cultureSeparator = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        normalized = text.Trim().Replace(".", cultureSeparator);
        return decimal.TryParse(normalized, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.CurrentCulture, out _);
    }

    private static PLCVariable FindVariable(string name)
    {
        DataAqc.EnsureInitialized();
        return DataAqc.PLCVariables.First(t => t.Name == name);
    }

    private static bool IsDataCollecting()
    {
        return bool.TryParse(FindVariable("数据采集标志").CurrentValue, out bool value) && value;
    }
}
