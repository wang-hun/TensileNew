using HandyControl.Data;
using Microsoft.Win32;
using NLog;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Xps.Packaging;
using TensileNeW.Models;
using TensileNeW.Services;
using Dialog = HandyControl.Controls.Dialog;
using Growl = HandyControl.Controls.Growl;
using MessageBox = HandyControl.Controls.MessageBox;

namespace TensileNeW;

public partial class MainWindow : Window
{
    private const string GrowlToken = "MainGrowl";
    private const int SettingsUnlockClickCount = 6;
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
    private readonly LoadPlotController _loadPlotController;
    private int _logoClickCount;
    private bool _autoTrackLatestPoint = true;
    private double _helpZoomFactor = 1.0;
    private string _lastHelpWebSearchKeyword = string.Empty;
    private Uri? _helpDocumentUri;
    private XpsDocument? _manualXpsDocument;
    public bool HasMissingManualOffice { get; set; }
    private readonly System.Windows.Threading.DispatcherTimer _loadScrollTimer;
    private readonly System.Windows.Threading.DispatcherTimer _plotAutoscaleTimer;
    private int _pendingLoadScrollIndex = -1;

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
    public static PLCVariable TensileDistanceLimitVariable => FindVariable("拉伸位移上限");

    public static PLCVariable StartPressCoil => FindVariable("压边线圈");
    public static PLCVariable ReleasePressCoil => FindVariable("压边释放线圈");
    public static PLCVariable StartTensileCoil => FindVariable("拉伸线圈");
    public static PLCVariable ReleaseTensileCoil => FindVariable("拉伸释放线圈");
    public static PLCVariable TanliaoVariable => FindVariable("弹料");
    public static PLCVariable CalibrationStateVariable => FindVariable("传感器标零状态");

    private static readonly HelpSearchModeOption[] HelpSearchModes =
    [
        new("当前页", false),
        new("全部页", true)
    ];

    public MainWindow(bool connectedAtStartup)
    {
        _connectedAtStartup = connectedAtStartup;
        _viewModel = new MainViewModel();
        _autoTrackLatestPoint = _viewModel.Setting.AutoTrackLatestPoint;
        _viewModel.RecipeWritten += name => Dispatcher.Invoke(() => ShowSuccess($"切换配方成功：{name}"));
        DataContext = _viewModel;
        InitializeComponent();
        _loadPlotController = new LoadPlotController(
            LoadPlot,
            () => _autoTrackLatestPoint,
            () => _viewModel.Setting.ShowPlotLegend,
            () => _viewModel.Setting.KeepPlotOnReset,
            22);
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
        _plotAutoscaleTimer.Tick += (_, _) => _loadPlotController.AutoScaleWhileCollecting();
        HelpSearchModeComboBox.ItemsSource = HelpSearchModes;
        HelpSearchModeComboBox.SelectedIndex = 1;
        UpdateHelpZoomText();
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

        if (HasMissingManualOffice)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ShowWarning(ManualDocumentService.MissingOfficeMessage);
                Dialog.Show(new ManualDocumentUnavailableDialog());
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
            if (item?.IsManualFile == true)
            {
                await OpenManualDocumentAsync(item);
                return;
            }

            ShowHelpWebView();
            Uri? targetUri = HelpDocumentLoader.TryBuildNavigationUri(item);
            if (targetUri is null)
            {
                return;
            }

            HelpSearchTextBox.Clear();

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

    private async Task OpenManualDocumentAsync(HelpNavigationItem item)
    {
        if (item.IsUnavailable)
        {
            ShowWarning(item.UnavailableMessage ?? ManualDocumentService.MissingOfficeMessage);
            return;
        }

        if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
        {
            ShowWarning("说明书文件不存在。");
            return;
        }

        if (ManualDocumentService.CanOpenInWebView(item.FilePath))
        {
            CloseManualXpsDocument();
            ShowHelpWebView();
            HelpWebView.Source = new Uri(item.FilePath);
            HelpSearchTextBox.Clear();
            return;
        }

        if (!ManualDocumentService.CanConvertToXps(item.FilePath))
        {
            ShowWarning("当前说明书格式不支持预览。");
            return;
        }

        HelpWebView.Visibility = Visibility.Collapsed;
            HelpDocumentViewer.Visibility = Visibility.Visible;

        ManualDocumentConvertResult result = !string.IsNullOrWhiteSpace(item.CachedPath) && File.Exists(item.CachedPath)
            ? ManualDocumentConvertResult.Ok(item.CachedPath)
            : await Task.Run(() => ManualDocumentService.ConvertToXpsFile(item.FilePath));
        if (!result.Success || string.IsNullOrWhiteSpace(result.XpsPath))
        {
            ShowHelpWebView();
            ShowWarning(result.Message ?? "说明书打开失败。");
            return;
        }

        try
        {
            item.CachedPath = result.XpsPath;
            CloseManualXpsDocument();
            _manualXpsDocument = new XpsDocument(result.XpsPath, FileAccess.Read);
            HelpDocumentViewer.Visibility = Visibility.Visible;
            HelpDocumentViewer.SetDocument(_manualXpsDocument);
            HelpDocumentViewer.SetZoomFactor(_helpZoomFactor);
        }
        catch (Exception ex)
        {
            ShowHelpWebView();
            ShowWarning($"说明书预览失败：{ex.Message}");
        }
    }

    private void ShowHelpWebView()
    {
        CloseManualXpsDocument();
        HelpDocumentViewer.Visibility = Visibility.Collapsed;
        HelpWebView.Visibility = Visibility.Visible;
    }

    private void CloseManualXpsDocument()
    {
        HelpDocumentViewer.ClearDocument();
        _manualXpsDocument?.Close();
        _manualXpsDocument = null;
    }

    private async void HelpSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        await ExecuteHelpSearchAsync(true, false);
        e.Handled = true;
    }

    private async void HelpSearchButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteHelpSearchAsync(true, false);
    }

    private async void HelpSearchPrevious_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteHelpSearchAsync(false, true);
    }

    private async void HelpSearchNext_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteHelpSearchAsync(true, true);
    }

    private async Task ExecuteHelpSearchAsync(bool forward, bool repeatLastSearch)
    {
        string keyword = repeatLastSearch ? _lastHelpWebSearchKeyword : HelpSearchTextBox.Text.Trim();
        if (!repeatLastSearch && string.IsNullOrWhiteSpace(keyword))
        {
            return;
        }

        bool searchWholeDocument = (HelpSearchModeComboBox.SelectedItem as HelpSearchModeOption)?.SearchWholeDocument == true;
        if (HelpDocumentViewer.Visibility == Visibility.Visible)
        {
            bool found = repeatLastSearch
                ? HelpDocumentViewer.RepeatLastSearch(forward, out string? message)
                : HelpDocumentViewer.Search(keyword, searchWholeDocument, forward, out message);
            if (!found && !string.IsNullOrWhiteSpace(message))
            {
                ShowInfo(message);
            }

            return;
        }

        if (HelpWebView.Visibility != Visibility.Visible)
        {
            return;
        }

        try
        {
            await HelpWebView.EnsureCoreWebView2Async();
            string effectiveKeyword = keyword;
            if (string.IsNullOrWhiteSpace(effectiveKeyword))
            {
                return;
            }

            _lastHelpWebSearchKeyword = effectiveKeyword;

            string serializedKeyword = JsonSerializer.Serialize(effectiveKeyword);
            string serializedForward = forward ? "true" : "false";
            string script = $$"""
                (() => {
                    const keyword = {{serializedKeyword}};
                    if (!window.find) {
                        return "unsupported";
                    }

                    const forward = {{serializedForward}};
                    const found = window.find(keyword, false, false, true, false, !forward, false);
                    return found ? "found" : "not-found";
                })();
                """;
            string result = await HelpWebView.ExecuteScriptAsync(script);
            if (string.Equals(result, "\"not-found\"", StringComparison.OrdinalIgnoreCase))
            {
                ShowInfo("未找到对应内容。");
            }
        }
        catch
        {
            ShowWarning("网页内容查找失败。");
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

        if (HelpDocumentViewer.Visibility == Visibility.Visible)
        {
            HelpDocumentViewer.SetZoomFactor(_helpZoomFactor);
            return;
        }

        try
        {
            HelpWebView.ZoomFactor = _helpZoomFactor;
        }
        catch
        {
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
        CloseManualXpsDocument();
        _viewModel.LoadItems.ListChanged -= LoadItems_ListChanged;
        _plotAutoscaleTimer.Stop();
        _viewModel.SaveSettings();
        MainViewModel.StopConsumers();
        TrialDataStore.TryDeleteDatabaseFile();
        Logger.Info("关闭程序");
    }

    private void InitializePlot()
    {
        _loadPlotController.Initialize(() => _loadPlotController.LocalizeContextMenu(OpenPlotWindow));
    }

    private void ResetPlot() => _loadPlotController.Reset();

    private void RefreshPlot() => _loadPlotController.Refresh();

    private void OpenPlotWindow()
    {
        if (_plotWindow is { IsVisible: true })
        {
            _plotWindow.Activate();
            return;
        }

        _plotWindow = new PlotWindow(
            () => _autoTrackLatestPoint,
            () => _viewModel.Setting.ShowPlotLegend,
            () => _viewModel.Setting.KeepPlotOnReset)
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

    private void ChartSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChartSettingsWindow(_viewModel.Setting, _viewModel.SaveSettings)
        {
            Owner = this
        };

        dialog.SettingsChanged += (_, _) =>
        {
            _autoTrackLatestPoint = _viewModel.Setting.AutoTrackLatestPoint;
            _loadPlotController.ApplyCurrentTheme();
            _plotWindow?.ApplyCurrentTheme();
        };
        dialog.Confirmed += (_, _) => ShowSuccess("设置成功");
        dialog.ResetSerialRequested += (_, _) => ShowResetSerialConfirmDialog();
        dialog.ShowDialog();
    }

    private void ShowResetSerialConfirmDialog()
    {
        var confirm = new ResetSerialConfirmDialog();
        confirm.Confirmed += (_, _) =>
        {
            _viewModel.ResetTrialSerialNumber();
            ShowSuccess("序列号已重置");
        };
        Dialog.Show(confirm);
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
        _loadPlotController.ApplyCurrentTheme();
        _plotWindow?.ApplyCurrentTheme();
        ShowSuccess($"已应用配色方案：{scheme.Name}");
    }

    private async void StartPress_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("压边");
    private async void ReleasePress_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("压边释放");
    private async void StartTensile_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("拉伸");
    private async void ReleaseTensile_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("拉伸释放");
    private async void Stop_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("停止");
    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.PulseAsync("数据重置");
        _viewModel.AdvanceTrialSerialNumber();
    }
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

    private void SaveData_Click(object sender, RoutedEventArgs e)
    {
        using var _ = BeginCurrentTrialPlotScope();
        _viewModel.SaveDataAs();
    }

    private async void SaveDataAndReport_Click(object sender, RoutedEventArgs e)
    {
        string recipeName = _viewModel.SelectedRecipe?.RecipeName ?? "NoRecipe";
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string baseFileName = $"{recipeName}_{SNModel.GetSn()}_{timestamp}";
        string folderPath = RAM.SettingModel.ExcelFolderPath;
        var waitWindow = new StartupWaitWindow("正在保存数据及试验报告，请稍后。");
        string? tempImagePath = null;

        try
        {
            IsEnabled = false;
            waitWindow.Show();
            await Task.Yield();

            Directory.CreateDirectory(folderPath);
            string excelPath = Path.Combine(folderPath, $"{baseFileName}.xlsx");
            string reportPath = Path.Combine(folderPath, $"{baseFileName}.docx");
            string trialSerialNumber = SNModel.GetSn();
            DateTime generatedAt = DateTime.Now;
            string maxForce = MaxForceVariable.CurrentValue;
            string validDistance = ValidDistanceVariable.CurrentValue;
            RecipeModel? recipe = _viewModel.SelectedRecipe;

            using (BeginCurrentTrialPlotScope())
            {
                tempImagePath = CaptureReportImageToTempFile();

                await Task.Run(() =>
                {
                    _viewModel.SaveDataToFile(excelPath);
                    SaveTestReportDocumentToFile(
                        reportPath,
                        tempImagePath,
                        recipeName,
                        trialSerialNumber,
                        generatedAt,
                        maxForce,
                        validDistance,
                        recipe);
                });
            }

            ShowSuccess("数据和试验报告保存成功");
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            ShowError("数据和试验报告保存失败");
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempImagePath) && File.Exists(tempImagePath))
            {
                File.Delete(tempImagePath);
            }

            waitWindow.Close();
            IsEnabled = true;
        }
    }

    private void GenerateTestReport_Click(object sender, RoutedEventArgs e)
    {
        string recipeName = _viewModel.SelectedRecipe?.RecipeName ?? "NoRecipe";
        var dialog = new SaveFileDialog
        {
            Filter = "Word (*.docx)|*.docx",
            InitialDirectory = RAM.SettingModel.ExcelFolderPath,
            FileName = $"{recipeName}_{SNModel.GetSn()}_{DateTime.Now:yyyyMMddHHmmss}"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using (BeginCurrentTrialPlotScope())
            {
                SaveTestReportToFile(dialog.FileName, recipeName);
            }
            ShowSuccess("试验报告保存成功");
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            ShowError("试验报告保存失败");
        }
    }

    private void SaveTestReportToFile(string fileName, string recipeName)
    {
        string? tempImagePath = null;
        try
        {
            tempImagePath = CaptureReportImageToTempFile();
            SaveTestReportDocumentToFile(
                fileName,
                tempImagePath,
                recipeName,
                SNModel.GetSn(),
                DateTime.Now,
                MaxForceVariable.CurrentValue,
                ValidDistanceVariable.CurrentValue,
                _viewModel.SelectedRecipe);
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempImagePath) && File.Exists(tempImagePath))
            {
                File.Delete(tempImagePath);
            }
        }
    }

    private string CaptureReportImageToTempFile()
    {
        InvokePlotMenuItem("自动缩放", "Autoscale");
        InvokePlotMenuItem("复制到剪贴板", "Copy to Clipboard");
        return TestReportService.SaveClipboardImageToTempFile();
    }

    private IDisposable BeginCurrentTrialPlotScope()
    {
        _loadPlotController.HideNonCurrentCurves();
        _plotWindow?.HideNonCurrentCurves();
        _loadPlotController.AutoScale();
        _plotWindow?.AutoScale();
        return new CurrentTrialPlotScope(this);
    }

    private void EndCurrentTrialPlotScope()
    {
        _loadPlotController.RestoreHiddenCurves();
        _plotWindow?.RestoreHiddenCurves();
        _loadPlotController.AutoScale();
        _plotWindow?.AutoScale();
    }

    private static void SaveTestReportDocumentToFile(
        string fileName,
        string imagePath,
        string recipeName,
        string trialSerialNumber,
        DateTime generatedAt,
        string maxForce,
        string validDistance,
        RecipeModel? recipe)
    {
        TestReportService.Save(
            fileName,
            imagePath,
            recipeName,
            trialSerialNumber,
            generatedAt,
            maxForce,
            validDistance,
            recipe);
    }

    private void InvokePlotMenuItem(params string[] labels)
    {
        var menu = LoadPlot.Menu;
        if (menu == null)
        {
            throw new InvalidOperationException("曲线图菜单不可用");
        }

        var item = menu.ContextMenuItems.FirstOrDefault(x => labels.Contains(x.Label));
        if (item.OnInvoke == null)
        {
            throw new InvalidOperationException($"曲线图菜单项不可用：{string.Join("/", labels)}");
        }

        item.OnInvoke.Invoke(LoadPlot.Plot);
    }

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
        dialog.Confirmed += (_, args) =>
        {
            string name = args.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowWarning("配方名称不能为空");
                return;
            }

            if (!_viewModel.AddRecipe(name, args.TemplateRecipe))
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

    private sealed record HelpSearchModeOption(string Title, bool SearchWholeDocument);

    private sealed class CurrentTrialPlotScope(MainWindow owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.EndCurrentTrialPlotScope();
        }
    }
}
