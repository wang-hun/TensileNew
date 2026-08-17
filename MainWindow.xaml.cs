using HandyControl.Data;
using Microsoft.Win32;
using Haukcode.HighResolutionTimer;
using NLog;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Xps.Packaging;
using TensileNeW.Models;
using TensileNeW.Services;
using TensileNeW.Tools;
using Dialog = HandyControl.Controls.Dialog;
using Growl = HandyControl.Controls.Growl;
using MessageBox = HandyControl.Controls.MessageBox;

namespace TensileNeW;

public partial class MainWindow : Window
{
    private const string GrowlToken = "MainGrowl";
    private const int SettingsUnlockClickCount = 6;
    private const double AppHeaderHeight = 39;
    private const double HelpZoomStep = 0.1;
    private const double HelpMinZoom = 0.5;
    private const double HelpMaxZoom = 2.0;
    private static readonly TimeSpan NetworkProbeUiTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NetworkAddressApplyDelay = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan NetworkConnectRetryInterval = TimeSpan.FromSeconds(1);
    private const int NetworkConnectRetryCount = 5;
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

    private static bool EnsureTrialDataSaveAvailable()
    {
        if (RAM.CanSaveTrialDataAndReport())
        {
            return true;
        }

        Dialog.Show(new TrialStartupNoticeDialog(RAM.TrialDataSaveCount, isDataSaveNotice: true));
        return false;
    }

    private static void ShowTrialDataSaveNotice(bool shouldShow)
    {
        if (shouldShow)
        {
            Dialog.Show(new TrialStartupNoticeDialog(RAM.TrialDataSaveCount, isDataSaveNotice: true));
        }
    }

    private static string GetWindowTitle()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string assemblyName = assembly.GetName().Name ?? "ECS";
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        string title = string.IsNullOrWhiteSpace(version)
            ? assemblyName
            : $"{assemblyName} {version}";
        return RAM.IsTrial ? $"{title}-试用版" : title;
    }

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly bool _connectedAtStartup;
    private readonly CameraStartupResult _cameraStartupResult;
    private readonly MainViewModel _viewModel;
    private VariableWindow? _variableWindow;
    private LoadDataWindow? _loadDataWindow;
    private PlotWindow? _plotWindow;
    private CameraPreviewWindow? _cameraPreviewWindow;
    private CameraCaptureService? _cameraCaptureService;
    private BitmapSource? _currentCameraBitmap;
    private bool _isClosing;
    private bool _shutdownCleanupStarted;
    private bool _runtimeDataSavePromptHandled;
    private bool _runtimeDataSavePromptOpen;
    private bool _runtimeDataSavePromptShouldSave;
    private bool _runtimeDataDeletePromptHandled;
    private bool _runtimeDataDeletePromptShouldDelete;
    private bool _isCameraReconnectRunning;
    private bool _autoSavePromptOpen;
    private bool _lastPlcConnected;
    private readonly LoadPlotController _loadPlotController;
    private TrialDataStore.TrialPlaybackData? _selectedPlaybackData;
    private long? _pendingPlaybackTrialGroupId;
    private int _logoClickCount;
    private bool _networkProbeRunning;
    private bool _autoTrackLatestPoint = true;
    private double _helpZoomFactor = 1.0;
    private HelpNavigationItem? _currentHelpItem;
    private XpsDocument? _manualXpsDocument;
    public bool HasMissingManualOffice { get; set; }
    private readonly System.Windows.Threading.DispatcherTimer _loadScrollTimer;
    private int _pendingLoadScrollIndex = -1;
    private const int FpsSampleWindowSize = 20;
    private readonly int[] _fpsSampleCounts = new int[FpsSampleWindowSize];
    private int _fpsSampleIndex;
    private int _fpsSampleTotal;

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

    public MainWindow(bool connectedAtStartup, CameraStartupResult? cameraStartupResult = null)
    {
        _connectedAtStartup = connectedAtStartup;
        _cameraStartupResult = cameraStartupResult ?? new CameraStartupResult([], null, null, null);
        _cameraCaptureService = _cameraStartupResult.CaptureService;
        _viewModel = new MainViewModel(_cameraStartupResult.Devices);
        _autoTrackLatestPoint = _viewModel.Setting.AutoTrackLatestPoint;
        _viewModel.RecipeWritten += name => Dispatcher.Invoke(() => ShowSuccess($"切换配方成功：{name}"));
        DataContext = _viewModel;
        InitializeComponent();
        _lastPlcConnected = string.Equals(DataAqc.plc.ConnectState, "true", StringComparison.OrdinalIgnoreCase);
        DataAqc.plc.PropertyChanged += Plc_PropertyChanged;
        DataAqc.DataCollectionEnded += OnDataCollectionEnded;
        DataAqc.UiBatchApplied += OnUiBatchApplied;
        Title = GetWindowTitle();
        _loadPlotController = new LoadPlotController(
            LoadPlot,
            () => _autoTrackLatestPoint,
            () => _viewModel.Setting.ShowPlotLegend,
            () => _viewModel.Setting.KeepPlotOnReset,
            11);
        _loadScrollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _loadScrollTimer.Tick += (_, _) =>
        {
            _loadScrollTimer.Stop();
            ScrollMainLoadDataToLatest();
        };
        HelpSearchModeComboBox.ItemsSource = HelpSearchModes;
        HelpSearchModeComboBox.SelectedIndex = 1;
        UpdateHelpZoomText();
        InitializeCameraPreview();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ConfigureWindowChrome();
        NativeTitleBarHelper.ApplyTheme(this);
    }

    private void ConfigureWindowChrome()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        double captionHeight = GetSystemCaptionHeight(hwnd);
        HeaderRowDefinition.Height = new GridLength(captionHeight + AppHeaderHeight);
        NativeTitleBarRowDefinition.Height = new GridLength(captionHeight);

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = captionHeight,
            GlassFrameThickness = new Thickness(0, captionHeight, 0, 0),
            ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness,
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = true
        });
    }

    private static double GetSystemCaptionHeight(IntPtr hwnd)
    {
        int dpi = GetDpiForWindow(hwnd);
        int captionPixels = GetSystemMetricsForDpiOrDefault(SM_CYCAPTION, dpi);
        int framePixels = GetSystemMetricsForDpiOrDefault(SM_CYFRAME, dpi);
        int paddedBorderPixels = GetSystemMetricsForDpiOrDefault(SM_CXPADDEDBORDER, dpi);

        return PixelsToDips(captionPixels + framePixels + paddedBorderPixels, dpi);
    }

    private static int GetSystemMetricsForDpiOrDefault(int index, int dpi)
    {
        if (dpi > 0)
        {
            try
            {
                return GetSystemMetricsForDpi(index, (uint)dpi);
            }
            catch (EntryPointNotFoundException ex)
            {
                Logger.Debug(ex, "系统 DPI API 不可用。");
            }
        }

        return GetSystemMetrics(index);
    }

    private static int GetDpiForWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            try
            {
                return (int)GetDpiForWindowNative(hwnd);
            }
            catch (EntryPointNotFoundException ex)
            {
                Logger.Debug(ex, "系统 DPI API 不可用。");
            }
        }

        return 96;
    }

    private static double PixelsToDips(int pixels, int dpi)
    {
        return dpi > 0
            ? pixels * 96.0 / dpi
            : pixels;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Logger.Info("启动程序");
        InitializePlot();
        ChartHintPanel.Visibility = _viewModel.Setting.HideChartHintOnStartup
            ? Visibility.Collapsed
            : Visibility.Visible;
        DataAqc.LoadDataBatchChanged += _ =>
        {
            RefreshPlot(autoScale: true);
        };
        DataAqc.ChartCleared += () => Dispatcher.Invoke(ResetPlot);
        _viewModel.LoadItems.ListChanged += LoadItems_ListChanged;
        if (_connectedAtStartup)
        {
            // 连接成功后先把当前选中配方写入并校验，再启动采集，避免启动采集读请求与配方写入交错。
            await WriteStartupRecipeAsync();
        }
        DataAqc.Refresh(Dispatcher);
        DataAqc.StartConsumers(Dispatcher);
        LoadHelpDocument();

        if (!_connectedAtStartup)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ShowError("连接失败，请检查线路！");
                ShowConnectionErrorDialog();
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

        if (!string.IsNullOrWhiteSpace(_cameraStartupResult.FailureMessage))
        {
            Dispatcher.BeginInvoke(() => ShowCameraConnectionError(_cameraStartupResult.FailureMessage));
        }

        Dispatcher.BeginInvoke(InitializeCameraAfterMainWindowShownAsync);
    }

    private void LoadHelpDocument()
    {
        try
        {
            HelpNavigationTree.ItemsSource = ManualDocumentService.LoadManualNavigation();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "加载说明书导航失败。");
            HelpNavigationTree.ItemsSource = null;
        }
    }

    private void OnUiBatchApplied(int sampleCount)
    {
        _fpsSampleTotal -= _fpsSampleCounts[_fpsSampleIndex];
        _fpsSampleCounts[_fpsSampleIndex] = sampleCount;
        _fpsSampleTotal += sampleCount;
        _fpsSampleIndex = (_fpsSampleIndex + 1) % FpsSampleWindowSize;
        FpsTextBlock.Text = _fpsSampleTotal.ToString("D2");
    }

    private void Plc_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DeltaPLC2.ConnectState))
        {
            return;
        }

        bool connected = string.Equals(DataAqc.plc.ConnectState, "true", StringComparison.OrdinalIgnoreCase);
        bool wasConnected = _lastPlcConnected;
        _lastPlcConnected = connected;
        if (wasConnected && !connected)
        {
            Dispatcher.BeginInvoke(() => ShowError("连接断开，请检查连接线路。"));
        }
    }

    private async Task WriteStartupRecipeAsync()
    {
        try
        {
            bool written = await _viewModel.WriteRecipeAsync();
            if (written)
            {
                Logger.Info("启动连接成功，已写入当前选中配方：{0}", _viewModel.SelectedRecipe?.RecipeName);
            }
            else
            {
                Logger.Warn("启动连接成功，但当前选中配方写入 PLC 失败：{0}", _viewModel.SelectedRecipe?.RecipeName);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "启动连接后写入当前选中配方失败。");
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
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "说明书导航失败，保留当前文档。");
            // Keep the current document unchanged if navigation fails.
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

        bool isPdf = ManualDocumentService.IsPdfManual(item.FilePath);
        if (!isPdf && !ManualDocumentService.CanConvertToXps(item.FilePath))
        {
            ShowWarning("当前说明书格式不支持预览。");
            return;
        }

        HelpDocumentViewer.Visibility = Visibility.Visible;
        if (isPdf)
        {
            try
            {
                CloseManualXpsDocument();
                _currentHelpItem = item;
                HelpDocumentViewer.Visibility = Visibility.Visible;
                OpenHelpSourceButton.Visibility = Visibility.Visible;
                HelpDocumentViewer.SetPdfDocument(item.FilePath);
                HelpDocumentViewer.SetZoomFactor(_helpZoomFactor);
                HelpSearchTextBox.Clear();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "打开 PDF 说明书预览失败。");
                HideHelpDocumentViewer();
                ShowWarning($"说明书预览失败：{ex.Message}");
            }

            return;
        }

        ManualDocumentConvertResult result = !string.IsNullOrWhiteSpace(item.CachedPath) && File.Exists(item.CachedPath)
            ? ManualDocumentConvertResult.Ok(item.CachedPath)
            : await Task.Run(() => ManualDocumentService.ConvertToXpsFile(item.FilePath));
        if (!result.Success || string.IsNullOrWhiteSpace(result.XpsPath))
        {
            HideHelpDocumentViewer();
            ShowWarning(result.Message ?? "说明书打开失败。");
            return;
        }

        try
        {
            item.CachedPath = result.XpsPath;
            CloseManualXpsDocument();
            _currentHelpItem = item;
            _manualXpsDocument = new XpsDocument(result.XpsPath, FileAccess.Read);
            HelpDocumentViewer.Visibility = Visibility.Visible;
            OpenHelpSourceButton.Visibility = Visibility.Visible;
            HelpDocumentViewer.SetDocument(_manualXpsDocument);
            HelpDocumentViewer.SetZoomFactor(_helpZoomFactor);
            HelpSearchTextBox.Clear();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "打开 XPS 说明书预览失败。");
            HideHelpDocumentViewer();
            ShowWarning($"说明书预览失败：{ex.Message}");
        }
    }

    private void HideHelpDocumentViewer()
    {
        CloseManualXpsDocument();
        HelpDocumentViewer.Visibility = Visibility.Collapsed;
        OpenHelpSourceButton.Visibility = Visibility.Collapsed;
        _currentHelpItem = null;
    }

    private void CloseManualXpsDocument()
    {
        HelpDocumentViewer.ClearDocument();
        _manualXpsDocument?.Close();
        _manualXpsDocument = null;
    }

    private void OpenCurrentManualFile_Click(object sender, RoutedEventArgs e)
    {
        string? filePath = _currentHelpItem?.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            ShowWarning("说明书文件不存在。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            if (OpenContainingFolder(filePath))
            {
                ShowWarning("打开原文件失败，已打开所在文件夹。");
                return;
            }

            ShowWarning($"打开原文件失败：{ex.Message}");
        }
    }

    private static bool OpenContainingFolder(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "打开说明书所在文件夹失败。");
            return false;
        }
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

    private Task ExecuteHelpSearchAsync(bool forward, bool repeatLastSearch)
    {
        string keyword = HelpSearchTextBox.Text.Trim();
        if (!repeatLastSearch && string.IsNullOrWhiteSpace(keyword))
        {
            return Task.CompletedTask;
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
        }

        return Task.CompletedTask;
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
        }
    }

    private void UpdateHelpZoomText()
    {
        HelpZoomBox.Text = $"{_helpZoomFactor:P0}";
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_runtimeDataSavePromptHandled && ShouldPromptRuntimeDataSaveOnExit())
        {
            e.Cancel = true;
            ShowRuntimeDataSavePrompt();
            return;
        }

        if (!_runtimeDataDeletePromptHandled && ShouldPromptRuntimeDataDeleteOnExit())
        {
            e.Cancel = true;
            ShowRuntimeDataDeletePrompt();
            return;
        }

        RunShutdownCleanup();
    }

    private void RunShutdownCleanup()
    {
        if (_shutdownCleanupStarted)
        {
            return;
        }

        _shutdownCleanupStarted = true;
        _isClosing = true;
        _variableWindow?.Close();
        _loadDataWindow?.Close();
        _plotWindow?.Close();
        _cameraPreviewWindow?.Close();
        ReleaseCameraInBackground();
        CloseManualXpsDocument();
        _viewModel.LoadItems.ListChanged -= LoadItems_ListChanged;
        DataAqc.UiBatchApplied -= OnUiBatchApplied;
        DataAqc.plc.PropertyChanged -= Plc_PropertyChanged;
        DataAqc.DataCollectionEnded -= OnDataCollectionEnded;
        _viewModel.SaveSettings();
        MainViewModel.StopConsumers();
        HandleRuntimeDataSaveOnExit();
        if (ShouldDeleteRuntimeDataOnExit())
        {
            TrialDataStore.TryDeleteDatabaseFile();
        }
        Logger.Info("关闭程序");
    }

    private bool ShouldPromptRuntimeDataSaveOnExit()
    {
        return !_runtimeDataSavePromptOpen
            && string.Equals(_viewModel.Setting.RuntimeDataSavePolicy, SettingModel.RuntimeDataSaveAskEveryTime, StringComparison.Ordinal)
            && TrialDataStore.HasAnyTrialData();
    }

    private bool ShouldPromptRuntimeDataDeleteOnExit()
    {
        return !_runtimeDataDeletePromptHandled
            && string.Equals(_viewModel.Setting.RuntimeDataDeletePolicy, SettingModel.RuntimeDataDeleteAskEveryTime, StringComparison.Ordinal)
            && TrialDataStore.HasAnyTrialData();
    }

    private bool ShouldDeleteRuntimeDataOnExit()
    {
        return _viewModel.Setting.RuntimeDataDeletePolicy == SettingModel.RuntimeDataDeleteAlwaysYes
            || (_runtimeDataDeletePromptHandled && _runtimeDataDeletePromptShouldDelete);
    }

    private void ShowRuntimeDataSavePrompt()
    {
        if (_runtimeDataSavePromptOpen)
        {
            return;
        }

        _runtimeDataSavePromptOpen = true;
        var dialog = new RuntimeDataSavePromptWindow
        {
            Owner = this
        };

        dialog.Closed += (_, _) =>
        {
            _runtimeDataSavePromptOpen = false;
            if (!dialog.HasDecision)
            {
                return;
            }

            _runtimeDataSavePromptHandled = true;
            _runtimeDataSavePromptShouldSave = dialog.ShouldSave;

            if (dialog.DontAskAgain)
            {
                _viewModel.Setting.RuntimeDataSavePolicy = dialog.ShouldSave
                    ? SettingModel.RuntimeDataSaveAlwaysYes
                    : SettingModel.RuntimeDataSaveAlwaysNo;
                _viewModel.SaveSettings();
            }

            Dispatcher.BeginInvoke(new Action(Close));
        };

        dialog.ShowDialog();
    }

    private void ShowRuntimeDataDeletePrompt()
    {
        if (_runtimeDataSavePromptOpen)
        {
            return;
        }

        _runtimeDataSavePromptOpen = true;
        var dialog = new RuntimeDataSavePromptWindow
        {
            Owner = this
        };
        dialog.ConfigurePrompt("删除运行数据", "是否删除当前所有试验的运行数据？");

        dialog.Closed += (_, _) =>
        {
            _runtimeDataSavePromptOpen = false;
            if (!dialog.HasDecision)
            {
                return;
            }

            _runtimeDataDeletePromptHandled = true;
            _runtimeDataDeletePromptShouldDelete = dialog.ShouldSave;

            if (dialog.DontAskAgain)
            {
                _viewModel.Setting.RuntimeDataDeletePolicy = dialog.ShouldSave
                    ? SettingModel.RuntimeDataDeleteAlwaysYes
                    : SettingModel.RuntimeDataDeleteAlwaysNo;
                _viewModel.SaveSettings();
            }

            Dispatcher.BeginInvoke(new Action(Close));
        };

        dialog.ShowDialog();
    }

    private void HandleRuntimeDataSaveOnExit()
    {
        try
        {
            if (!TrialDataStore.HasAnyTrialData())
            {
                return;
            }

            string policy = _viewModel.Setting.RuntimeDataSavePolicy;
            bool shouldSave = policy == SettingModel.RuntimeDataSaveAlwaysYes ||
                (_runtimeDataSavePromptHandled && _runtimeDataSavePromptShouldSave);

            if (!shouldSave)
            {
                return;
            }

            string? exportedPath = TrialDataStore.ExportDatabaseCopy(_viewModel.Setting.ExcelFolderPath);
            if (string.IsNullOrWhiteSpace(exportedPath))
            {
                MessageBox.Error("运行数据保存失败。", "保存运行数据");
                return;
            }

            Logger.Info($"运行数据已保存到：{exportedPath}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "程序退出时保存运行数据失败。");
            MessageBox.Error("运行数据保存失败。", "保存运行数据");
        }
    }

    private void InitializePlot()
    {
        _loadPlotController.Initialize(() => _loadPlotController.LocalizeContextMenu(
            OpenPlotWindow,
            () => ShowCurveFilterDialog(_loadPlotController),
            () => ShowClearPlotConfirmDialog(_loadPlotController)));
    }

    private void ResetPlot() => _loadPlotController.Reset();

    private void RefreshPlot(bool autoScale = false) => _loadPlotController.Refresh(autoScale);

    private void InitializeCameraPreview()
    {
        if (_cameraCaptureService is null)
        {
            SetCameraPreviewSource(null);
            return;
        }

        _cameraCaptureService.SetBitmapDispatcher(Dispatcher);
        _cameraCaptureService.FrameArrived += CameraCaptureService_FrameArrived;
        _cameraCaptureService.CaptureFailed += CameraCaptureService_CaptureFailed;
        SetCameraPreviewSource(_cameraCaptureService.CurrentBitmap);
        ReportHomeCameraDisplayDemand();
    }

    private async Task InitializeCameraAfterMainWindowShownAsync()
    {
        if (_cameraStartupResult.Devices.Count == 0 || !string.IsNullOrWhiteSpace(_cameraStartupResult.FailureMessage))
        {
            return;
        }

        if (_viewModel.SelectedCameraDevice is null)
        {
            ShowCameraSelectionDialog();
            return;
        }

        await ApplySelectedCameraAsync();
    }

    private void ShowCameraSelectionDialog()
    {
        CameraCaptureService cameraService = EnsureCameraCaptureService();
        CameraSelectionWindow dialog = new(_cameraStartupResult.Devices, cameraService);
        dialog.Confirmed += (_, args) =>
        {
            _viewModel.SelectedCameraDevice = args.Device;
            _viewModel.SaveSettings();
            AttachActiveCameraService(args.Device);
        };

        Dialog.Show(dialog);
    }

    private CameraCaptureService EnsureCameraCaptureService()
    {
        _cameraCaptureService ??= new CameraCaptureService();
        return _cameraCaptureService;
    }

    private void AttachActiveCameraService(CameraDeviceDescriptor selectedDevice)
    {
        if (_cameraCaptureService is null)
        {
            return;
        }

        _cameraCaptureService.FrameArrived -= CameraCaptureService_FrameArrived;
        _cameraCaptureService.CaptureFailed -= CameraCaptureService_CaptureFailed;
        _cameraCaptureService.FrameArrived += CameraCaptureService_FrameArrived;
        _cameraCaptureService.CaptureFailed += CameraCaptureService_CaptureFailed;
        SetCameraPreviewSource(_cameraCaptureService.CurrentBitmap);
        ShowSuccess($"摄像头已连接：{selectedDevice.Name}");
    }

    private void CameraCaptureService_FrameArrived(object? sender, CameraFrameArrivedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => CameraCaptureService_FrameArrived(sender, e));
            return;
        }

        SetCameraPreviewSource(e.Bitmap);
    }

    private void CameraCaptureService_CaptureFailed(object? sender, Exception e)
    {
        Logger.Error(e);
    }

    private const string HomeCameraDemandKey = "home-preview";

    /// <summary>
    /// Tells the capture service how many device pixels the home preview actually
    /// occupies, so the streamed frame size follows the real layout instead of a
    /// hardcoded resolution.
    /// </summary>
    private void ReportHomeCameraDisplayDemand()
    {
        CameraCaptureService? camera = _cameraCaptureService;
        if (camera is null)
        {
            return;
        }

        if (HomeCameraPreviewImage.ActualWidth <= 0 || HomeCameraPreviewImage.ActualHeight <= 0)
        {
            return;
        }

        double scaleX = 1.0;
        double scaleY = 1.0;
        PresentationSource source = PresentationSource.FromVisual(HomeCameraPreviewImage);
        if (source?.CompositionTarget is not null)
        {
            scaleX = source.CompositionTarget.TransformToDevice.M11;
            scaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        camera.ReportDisplayDemand(
            HomeCameraDemandKey,
            (int)Math.Ceiling(HomeCameraPreviewImage.ActualWidth * scaleX),
            (int)Math.Ceiling(HomeCameraPreviewImage.ActualHeight * scaleY));
    }

    private void HomeCameraPreviewImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ReportHomeCameraDisplayDemand();
    }

    private void SetCameraPreviewSource(BitmapSource? source)
    {
        _currentCameraBitmap = source;
        HomeCameraPreviewImage.Source = source;
        HomeCameraEmptyTextBlock.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        _cameraPreviewWindow?.SetPreviewSource(source);
    }

    private async Task ApplySelectedCameraAsync(bool showErrorDialog = true, bool showSuccessMessage = true)
    {
        CameraDeviceDescriptor? selectedDevice = _viewModel.SelectedCameraDevice;
        if (selectedDevice is null)
        {
            if (_cameraCaptureService is not null)
            {
                _cameraCaptureService.FrameArrived -= CameraCaptureService_FrameArrived;
                _cameraCaptureService.CaptureFailed -= CameraCaptureService_CaptureFailed;
                await _cameraCaptureService.DisposeAsync();
                _cameraCaptureService = null;
            }

            SetCameraPreviewSource(null);
            return;
        }

        _cameraCaptureService = EnsureCameraCaptureService();
        _cameraCaptureService.FrameArrived -= CameraCaptureService_FrameArrived;
        _cameraCaptureService.CaptureFailed -= CameraCaptureService_CaptureFailed;
        _cameraCaptureService.SetBitmapDispatcher(Dispatcher);

        try
        {
            CameraCaptureService cameraCaptureService = _cameraCaptureService;
            await Task.Run(async () => await cameraCaptureService.StartAsync(selectedDevice.Id, Dispatcher));
            if (_isClosing)
            {
                return;
            }

            _cameraCaptureService.FrameArrived += CameraCaptureService_FrameArrived;
            _cameraCaptureService.CaptureFailed += CameraCaptureService_CaptureFailed;
            if (showSuccessMessage)
            {
                ShowSuccess($"摄像头已连接：{selectedDevice.Name}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            SetCameraPreviewSource(null);
            string message = $"{selectedDevice.Name}摄像头连接失败：{ex.Message}";
            if (showErrorDialog)
            {
                ShowCameraConnectionError(message);
            }
            else
            {
                ShowError(message);
            }
        }
    }

    private async void RefreshCameraButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cameraCaptureService?.IsRunning == true && _currentCameraBitmap is not null)
        {
            return;
        }

        if (_isCameraReconnectRunning)
        {
            ShowInfo("摄像头正在重连，请稍后");
            return;
        }

        if (_viewModel.SelectedCameraDevice is null)
        {
            ShowWarning("请先选择摄像头");
            return;
        }

        _isCameraReconnectRunning = true;
        RefreshCameraButton.IsEnabled = false;
        try
        {
            ShowInfo("正在重新连接摄像头");
            await ApplySelectedCameraAsync(showErrorDialog: false);
        }
        finally
        {
            RefreshCameraButton.IsEnabled = true;
            _isCameraReconnectRunning = false;
        }
    }

    private void ShowCameraConnectionError(string message)
    {
        ShowError(message);
        MessageBox.Error(message, "摄像头连接失败");
    }

    private void ReleaseCameraInBackground()
    {
        CameraCaptureService? camera = _cameraCaptureService;
        _cameraCaptureService = null;
        if (camera is null)
        {
            return;
        }

        camera.FrameArrived -= CameraCaptureService_FrameArrived;
        camera.CaptureFailed -= CameraCaptureService_CaptureFailed;
        _ = Task.Run(async () =>
        {
            try
            {
                await camera.DisposeAsync();
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        });
    }

    private void HomeCameraPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        OpenCameraPreviewWindow();
    }

    private void OpenCameraPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        OpenCameraPreviewWindow();
    }

    private void OpenCameraPreviewWindow()
    {
        if (_cameraPreviewWindow is { IsVisible: true })
        {
            _cameraPreviewWindow.Activate();
            return;
        }

        _cameraPreviewWindow = new CameraPreviewWindow
        {
            Owner = this
        };
        _cameraPreviewWindow.Closed += (_, _) => _cameraPreviewWindow = null;
        _cameraPreviewWindow.SetPreviewSource(_currentCameraBitmap);
        _cameraPreviewWindow.Show();
        _cameraPreviewWindow.AttachCaptureService(_cameraCaptureService);
    }

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

    private void ShowCurveFilterDialog(LoadPlotController plotController)
    {
        IReadOnlyList<LoadPlotController.CurveFilterEntry> entries = plotController.GetCurveFilterEntries();
        if (entries.Count == 0)
        {
            ShowWarning("当前没有可筛选的曲线。");
            return;
        }

        var dialog = new CurveFilterWindow(entries)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            plotController.ApplyCurveFilter(dialog.GetSelections());
        }
    }

    private void ShowClearPlotConfirmDialog(LoadPlotController plotController)
    {
        if (!plotController.HasCurves)
        {
            ShowWarning("当前曲线图没有可清空的曲线。");
            return;
        }

        var dialog = new ClearPlotConfirmDialog();
        dialog.Confirmed += (_, _) => plotController.ClearCurrentPlotCurves();
        Dialog.Show(dialog);
    }

    private void Home_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Home";
    private void Settings_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Settings";
    private void Help_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Help";
    private async void Playback_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CurrentPage = "Playback";
        await RefreshPlaybackTrialsAsync();
    }
    private void Variables_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Variables";
    private void ColorSchemes_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "ColorSchemes";

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        ReconnectButton.IsEnabled = false;
        IsEnabled = false;
        bool connected = false;

        var waitWindow = new StartupWaitWindow(GetConnectWaitText());

        try
        {
            waitWindow.Show();
            var reconnectTask = TryReconnectAsync();
            await Task.WhenAll(reconnectTask, Task.Delay(TimeSpan.FromSeconds(2)));
            connected = await reconnectTask;
        }
        finally
        {
            waitWindow.Close();
            IsEnabled = true;
            ReconnectButton.IsEnabled = true;
        }

        if (!connected)
        {
            ShowError("\u8fde\u63a5\u5931\u8d25\uff0c\u8bf7\u68c0\u67e5\u7ebf\u8def\uff01");
            _ = Dispatcher.BeginInvoke(ShowConnectionErrorDialog);
        }
    }

    private static string GetConnectWaitText()
    {
        return string.Equals(RAM.SettingModel.Language, "EN", StringComparison.OrdinalIgnoreCase)
            ? "Connecting to device host, please wait..."
            : "正在连接 设备主机，请稍后...";
    }

    private static async Task<bool> TryReconnectAsync()
    {
        try
        {
            return await Task.Run(DataAqc.TryReconnect);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "网络探测失败。");
            return false;
        }
    }

    /// <summary>
    /// 探测期间在单个候选 IP 上做有限次重试，覆盖刚 add address 后 Windows
    /// 路由表/源地址选择尚未就绪的窗口；超出整体探测超时立即放弃。
    /// </summary>
    private static async Task<bool> TryConnectWithRetriesAsync(Stopwatch probeStopwatch)
    {
        for (int attempt = 0; attempt < NetworkConnectRetryCount; attempt++)
        {
            if (probeStopwatch.Elapsed >= NetworkProbeUiTimeout)
            {
                return false;
            }

            if (await TryReconnectAsync())
            {
                return true;
            }

            if (attempt < NetworkConnectRetryCount - 1)
            {
                await Task.Delay(NetworkConnectRetryInterval);
            }
        }

        return false;
    }

    private void ShowConnectionErrorDialog()
    {
        Dialog.Show(new ConnectionErrorDialog(RunNetworkProbeAndReconnectAsync));
    }

    private async Task RunNetworkProbeAndReconnectAsync()
    {
        if (_networkProbeRunning)
        {
            return;
        }

        _networkProbeRunning = true;
        ReconnectButton.IsEnabled = false;
        IsEnabled = false;
        string? successMessage = null;
        string? warningMessage = null;
        string? errorMessage = null;
        bool showFailureDialog = false;

        var waitWindow = new StartupWaitWindow("正在探测有线网络并尝试连接设备，请稍后...");
        DataAqc.AutoReconnectSuspended = true;
        try
        {
            waitWindow.Show();
            IReadOnlyList<NetworkProbeCandidate> candidates = NetworkAdapterProbeService.BuildProbeCandidates(RAM.SettingModel.PLC_IP);
            if (candidates.Count == 0)
            {
                warningMessage = "未发现可用于探测的有线网卡。";
            }
            else
            {
                Stopwatch probeStopwatch = Stopwatch.StartNew();
                NetworkProbeResult? lastFailure = null;
                foreach (NetworkProbeCandidate candidate in candidates)
                {
                    if (probeStopwatch.Elapsed >= NetworkProbeUiTimeout)
                    {
                        warningMessage = "网络探测超时，请检查设备线路后重试。";
                        break;
                    }

                    waitWindow.SetWaitText($"正在配置 {candidate.AdapterName}，请稍后...");
                    NetworkProbeResult addResult = await NetworkAdapterProbeService.RunElevatedAddAddressAsync(candidate);
                    if (!addResult.Success)
                    {
                        lastFailure = addResult;
                        continue;
                    }

                    await Task.Delay(NetworkAddressApplyDelay);
                    waitWindow.SetWaitText($"正在通过 {candidate.AdapterName} 连接设备...");
                    bool connected = await TryConnectWithRetriesAsync(probeStopwatch);
                    if (connected)
                    {
                        // 探测重连期间采集已暂停；等待配方写入完成后再恢复采集。
                        await WriteStartupRecipeAsync();
                        successMessage = $"网络检查成功，已通过 {candidate.AdapterName} 连接设备。";
                        break;
                    }

                    lastFailure = new NetworkProbeResult
                    {
                        Success = false,
                        AdapterName = candidate.AdapterName,
                        LocalIp = candidate.LocalIp,
                        Message = $"已配置 {candidate.AdapterName}，但设备连接失败。"
                    };

                    waitWindow.SetWaitText($"正在清理 {candidate.AdapterName}，请稍后...");
                    await NetworkAdapterProbeService.RunElevatedRemoveAddressAsync(candidate);
                }

                if (string.IsNullOrWhiteSpace(successMessage) && string.IsNullOrWhiteSpace(warningMessage))
                {
                    errorMessage = lastFailure?.Message ?? "所有有线网卡均未连接到设备。";
                    showFailureDialog = true;
                }
            }
        }
        finally
        {
            DataAqc.AutoReconnectSuspended = false;
            if (waitWindow.IsVisible)
            {
                waitWindow.Close();
            }

            IsEnabled = true;
            ReconnectButton.IsEnabled = true;
            _networkProbeRunning = false;
        }

        if (!string.IsNullOrWhiteSpace(successMessage))
        {
            ShowSuccess(successMessage);
        }

        if (!string.IsNullOrWhiteSpace(warningMessage))
        {
            ShowWarning(warningMessage);
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            ShowError(errorMessage);
        }

        if (showFailureDialog)
        {
            _ = Dispatcher.BeginInvoke(ShowConnectionErrorDialog);
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

        var dialog = new SettingsPinWindow
        {
            Owner = this
        };
        dialog.Unlocked += (_, _) =>
        {
            VariablesButton.Visibility = Visibility.Visible;
        };
        dialog.ColorUnlocked += (_, _) =>
        {
            ColorSchemesButton.Visibility = Visibility.Visible;
            _viewModel.CurrentPage = "ColorSchemes";
        };
        dialog.DebugAlgorithmDataRequested += (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(async () => await RunDebugAlgorithmDataImportAsync()));
        };
        dialog.ShowDialog();
    }

    private async Task RunDebugAlgorithmDataImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择原始数据 Excel",
            Filter = "Excel 文件 (*.xlsx;*.xls)|*.xlsx;*.xls",
            Multiselect = false
        };

        if (Directory.Exists(RAM.SettingModel.ExcelFolderPath))
        {
            dialog.InitialDirectory = RAM.SettingModel.ExcelFolderPath;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            double displacementStep = DisplacementResamplingService.GetDisplacementStep(
                ResolveAlgorithmSpeed(_viewModel.SelectedRecipe));
            string outputFileName = await Task.Run(() =>
                DebugAlgorithmExcelService.CreateDebugIntegratedDataFile(dialog.FileName, displacementStep));

            ShowSuccess($"整合数据已生成：{Path.GetFileName(outputFileName)}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            ShowError("原始数据整合处理失败");
        }
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
    private async void ReleaseTensile_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.PulseAsync("拉伸释放");
        if (ReleaseTensileResetCheckBox.IsChecked == true)
        {
            await _viewModel.PulseAsync("数据重置");
        }
    }
    private async void Stop_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("停止");
    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.PulseAsync("数据重置");
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
            TrialDataStore.RecordRecipeVersion(_viewModel.SelectedRecipe);
            string name = _viewModel.SelectedRecipe?.RecipeName ?? string.Empty;
            ShowSuccess($"已写入配置参数：{name}");
        }
        else
        {
            ShowError("写入配置参数失败，请检查连接");
        }
    }
    private async void ClosePress_Down(object sender, MouseButtonEventArgs e) => await _viewModel.SetStrokeStampingAsync(true);
    private async void ClosePress_Up(object sender, MouseButtonEventArgs e) => await _viewModel.SetStrokeStampingAsync(false);
    private async void Tanliao_Down(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("弹料", true);
    private async void Tanliao_Up(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("弹料", false);

    private void SaveData_Click(object sender, RoutedEventArgs e)
    {
        using var _ = BeginCurrentTrialPlotScope();
        if (_viewModel.SaveDataAs())
        {
            _viewModel.AdvanceTrialSerialNumber();
        }
    }

    private void OpenDataSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folderPath = RAM.SettingModel.ExcelFolderPath;
            Directory.CreateDirectory(folderPath);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            ShowError("打开数据保存文件夹失败");
        }
    }

    private async void RefreshPlayback_Click(object sender, RoutedEventArgs e) => await RefreshPlaybackTrialsAsync();

    private async Task RefreshPlaybackTrialsAsync()
    {
        long? selectedId = (PlaybackTrialListBox.SelectedItem as TrialDataStore.TrialPlaybackSummary)?.TrialGroupId;
        IReadOnlyList<TrialDataStore.TrialPlaybackSummary> summaries = await Task.Run(TrialDataStore.GetTrialPlaybackSummaries);
        PlaybackTrialListBox.ItemsSource = summaries;

        TrialDataStore.TrialPlaybackSummary? selected = summaries.FirstOrDefault(item => item.TrialGroupId == selectedId);
        if (selected != null)
        {
            PlaybackTrialListBox.SelectedItem = selected;
        }
        else if (summaries.Count > 0)
        {
            PlaybackTrialListBox.SelectedIndex = 0;
        }
        else
        {
            ClearPlaybackDisplay();
        }
    }

    private async void PlaybackTrialListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PlaybackTrialListBox.SelectedItem is not TrialDataStore.TrialPlaybackSummary summary)
        {
            ClearPlaybackDisplay();
            return;
        }

        _pendingPlaybackTrialGroupId = summary.TrialGroupId;
        TrialDataStore.TrialPlaybackData? data = await Task.Run(() => TrialDataStore.GetTrialPlaybackData(summary.TrialGroupId));
        if (_pendingPlaybackTrialGroupId != summary.TrialGroupId)
        {
            return;
        }

        if (data == null)
        {
            ClearPlaybackDisplay();
            ShowWarning("读取该次试验数据失败。");
            return;
        }

        _selectedPlaybackData = data;
        RenderPlayback(data);
    }

    private void ClearPlaybackDisplay()
    {
        _selectedPlaybackData = null;
        _pendingPlaybackTrialGroupId = null;
        PlaybackPlot.Plot.Clear();
        PlaybackPlot.Refresh();
        PlaybackTrialTitleTextBlock.Text = string.Empty;
        PlaybackRecipeNameTextBlock.Text = "-";
        PlaybackMaxForceTextBlock.Text = "-";
        PlaybackStrokePressTextBlock.Text = "-";
        PlaybackMaxDistanceTextBlock.Text = "-";
        PlaybackClosedLoopTextBlock.Text = "-";
        PlaybackSpeedTextBlock.Text = "-";
        PlaybackShutdownTextBlock.Text = "-";
        PlaybackDistanceLimitTextBlock.Text = "-";
    }

    private void RenderPlayback(TrialDataStore.TrialPlaybackData data)
    {
        PlaybackPlot.Plot.Clear();
        if (data.Points.Count > 0)
        {
            var scatter = PlaybackPlot.Plot.Add.Scatter(
                data.Points.Select(point => (double)point.RealDistance).ToArray(),
                data.Points.Select(point => (double)point.RealForce).ToArray());
            scatter.Smooth = true;
            scatter.MarkerSize = 0;
            scatter.Color = ScottPlot.Color.FromHex("#003A8C");
            PlaybackPlot.Plot.Axes.AutoScale();
        }

        PlaybackPlot.Plot.Title("力位移数据", 15);
        PlaybackPlot.Plot.XLabel("位移（mm）", 15);
        PlaybackPlot.Plot.YLabel("力（KN）", 15);
        PlaybackPlot.Plot.Axes.Bottom.TickLabelStyle.FontSize = 11;
        PlaybackPlot.Plot.Axes.Left.TickLabelStyle.FontSize = 11;
        PlaybackPlot.Plot.Axes.Bottom.TickGenerator.MaxTickCount = 6;
        PlaybackPlot.Plot.Axes.Left.TickGenerator.MaxTickCount = 6;
        PlaybackPlot.Refresh();

        RecipeModel? recipe = data.Recipe;
        double maxForce = data.Points.Count == 0 ? 0 : data.Points.Max(point => point.RealForce);
        double maxDistance = data.Points.Count == 0 ? 0 : data.Points.Max(point => point.RealDistance);
        PlaybackTrialTitleTextBlock.Text = $"序号：{data.Summary.TrialSerialNumber}    {data.Summary.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        PlaybackRecipeNameTextBlock.Text = recipe?.RecipeName ?? "未记录配方";
        PlaybackMaxForceTextBlock.Text = $"{maxForce:F3} KN";
        PlaybackStrokePressTextBlock.Text = recipe == null ? "-" : $"{recipe.StrokeStampingForce:F3} KN";
        PlaybackMaxDistanceTextBlock.Text = $"{maxDistance:F3} mm";
        PlaybackClosedLoopTextBlock.Text = recipe == null ? "-" : $"{recipe.ClosedLoopStampingForce:F3} KN";
        PlaybackSpeedTextBlock.Text = recipe == null ? "-" : $"{recipe.Speed:F3} mm/s";
        PlaybackShutdownTextBlock.Text = recipe == null ? "-" : $"{recipe.ShutdownDelay} s / {recipe.ShutdownRatio:F3}";
        PlaybackDistanceLimitTextBlock.Text = recipe == null ? "-" : $"{recipe.TensileDistanceLimit:F3} mm";
    }

    private async void SavePlaybackData_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureTrialDataSaveAvailable())
        {
            return;
        }

        bool shouldShowTrialDataSaveNotice = RAM.IsTrial && RAM.TrialDataSaveCount is 9 or 24 or 39 or 49 or 74 or 89 or 99;
        bool saved = false;
        TrialDataStore.TrialPlaybackData? data = _selectedPlaybackData;
        if (data == null || data.Points.Count == 0)
        {
            ShowWarning("请先选择一条包含数据的历史试验。");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            InitialDirectory = RAM.SettingModel.ExcelFolderPath,
            FileName = BuildPlaybackBaseFileName(data)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            bool saveIntegratedData = PlaybackAlgorithmIntegratedDataCheckBox.IsChecked == true;
            string integratedPath = Path.Combine(
                Path.GetDirectoryName(dialog.FileName) ?? RAM.SettingModel.ExcelFolderPath,
                $"{Path.GetFileNameWithoutExtension(dialog.FileName)}_算法整合数据.xlsx");
            double displacementStep = DisplacementResamplingService.GetDisplacementStep(
                data.Recipe?.Speed > 0 ? data.Recipe.Speed : DisplacementResamplingService.DefaultSpeed);

            await Task.Run(() =>
            {
                SavePlaybackDataToFile(dialog.FileName, data.Points);
                if (saveIntegratedData)
                {
                    DisplacementResamplingService.SaveResampledDataToFile(integratedPath, data.Points, displacementStep);
                }
            });
            RAM.RecordTrialDataAndReportSaved();
            _viewModel.RefreshTrialPackageInfo();
            ShowSuccess("回放数据表格保存成功");
            saved = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "保存回放数据表格失败。");
            ShowError("回放数据表格保存失败");
        }

        if (saved)
        {
            ShowTrialDataSaveNotice(shouldShowTrialDataSaveNotice);
        }
    }

    private async void SavePlaybackReport_Click(object sender, RoutedEventArgs e)
    {
        TrialDataStore.TrialPlaybackData? data = _selectedPlaybackData;
        if (data == null || data.Points.Count == 0)
        {
            ShowWarning("请先选择一条包含数据的历史试验。");
            return;
        }

        string? tempImagePath = null;
        var waitWindow = new StartupWaitWindow("正在保存回放试验报告，请稍后。");
        try
        {
            IsEnabled = false;
            waitWindow.Show();
            await Task.Yield();

            string folderPath = RAM.SettingModel.ExcelFolderPath;
            Directory.CreateDirectory(folderPath);
            string baseFileName = BuildPlaybackBaseFileName(data);
            string reportPath = Path.Combine(folderPath, $"{baseFileName}.docx");
            tempImagePath = CapturePlaybackPlotImageToTempFile();
            double maxForce = data.Points.Max(point => point.RealForce);
            double maxDistance = data.Points.Max(point => point.RealDistance);

            await Task.Run(() =>
            {
                SaveTestReportDocumentToFile(
                    reportPath,
                    tempImagePath,
                    data.Recipe?.RecipeName ?? "NoRecipe",
                    data.Summary.TrialSerialNumber,
                    data.Summary.StartedAtUtc.ToLocalTime(),
                    maxForce.ToString("F3", CultureInfo.InvariantCulture),
                    maxDistance.ToString("F3", CultureInfo.InvariantCulture),
                    data.Recipe);
            });

            ShowSuccess("回放试验报告保存成功");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "保存回放试验报告失败。");
            ShowError("回放试验报告保存失败");
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

    private static string BuildPlaybackBaseFileName(TrialDataStore.TrialPlaybackData data)
    {
        string recipeName = string.IsNullOrWhiteSpace(data.Recipe?.RecipeName) ? "NoRecipe" : data.Recipe.RecipeName;
        return $"{data.Summary.TrialSerialNumber}_{recipeName}_回放_{data.Summary.StartedAtUtc.ToLocalTime():yyyyMMddHHmmss}";
    }

    private static void SavePlaybackDataToFile(string fileName, IReadOnlyList<Loadmodel> points)
    {
        using var exporter = new ExcelExporter_EPPlus();
        exporter.CreateSheet("Orders")
            .SetHeader(new[] { "序号", "位移(mm)", "力(kN)", "压边(kN)", "时间(s)" })
            .AddData(points, point => new object[] { point.Index, point.RealDistance, point.RealForce, point.RealPress, point.Time })
            .SaveToFile(fileName);
    }

    private string CapturePlaybackPlotImageToTempFile()
    {
        string tempImagePath = Path.Combine(Path.GetTempPath(), $"TensilePlaybackReport_{Guid.NewGuid():N}.png");
        PlaybackPlot.Plot.SavePng(tempImagePath, 1200, 700);
        return tempImagePath;
    }

    private async void SaveDataAndReport_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureTrialDataSaveAvailable())
        {
            return;
        }

        if (DataAqc.loadModels.Count == 0)
        {
            return;
        }

        bool shouldShowTrialDataSaveNotice = RAM.IsTrial && RAM.TrialDataSaveCount is 9 or 24 or 39 or 49 or 74 or 89 or 99;
        string recipeName = _viewModel.SelectedRecipe?.RecipeName ?? "NoRecipe";
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string baseFileName = $"{SNModel.GetSn()}_{recipeName}_{timestamp}";
        string folderPath = RAM.SettingModel.ExcelFolderPath;
        using var waitWindow = new BackgroundStartupWaitWindow("正在保存数据及试验报告，请稍后。");
        string? tempImagePath = null;
        bool saved = false;

        try
        {
            IsEnabled = false;
            await waitWindow.ShowAsync();

            Directory.CreateDirectory(folderPath);
            string excelPath = Path.Combine(folderPath, $"{baseFileName}.xlsx");
            string algorithmIntegratedDataPath = Path.Combine(folderPath, $"{baseFileName}_算法整合数据.xlsx");
            string reportPath = Path.Combine(folderPath, $"{baseFileName}.docx");
            string trialSerialNumber = SNModel.GetSn();
            DateTime generatedAt = DateTime.Now;
            RecipeModel? recipe = _viewModel.SelectedRecipe;
            bool saveAlgorithmIntegratedData = AlgorithmIntegratedDataCheckBox.IsChecked == true;
            var dataSnapshot = DataAqc.loadModels.ToList();
            if (dataSnapshot.Count == 0)
            {
                return;
            }
            string maxForce = GetMaxForceText(dataSnapshot);
            string validDistance = GetMaxDistanceText(dataSnapshot);
            double algorithmDisplacementStep = DisplacementResamplingService.GetDisplacementStep(
                ResolveAlgorithmSpeed(recipe));

            using (BeginCurrentTrialPlotScope())
            {
                tempImagePath = CaptureReportImageToTempFile();

                await Task.Run(() =>
                {
                    _viewModel.SaveDataToFile(excelPath, dataSnapshot);
                    if (saveAlgorithmIntegratedData)
                    {
                        DisplacementResamplingService.SaveResampledDataToFile(
                            algorithmIntegratedDataPath,
                            dataSnapshot,
                            algorithmDisplacementStep);
                    }

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

            RAM.RecordTrialDataAndReportSaved();
            _viewModel.RefreshTrialPackageInfo();
            ShowSuccess("数据和试验报告保存成功");
            saved = true;
            _viewModel.AdvanceTrialSerialNumber();
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

            IsEnabled = true;
        }

        if (saved)
        {
            ShowTrialDataSaveNotice(shouldShowTrialDataSaveNotice);
        }
    }

    private void OnDataCollectionEnded()
    {
        // The existing consumer has already queued its UI updates before this state transition.
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(async () =>
        {
            await DelayOneSecondWithHighResolutionTimerAsync();

            if (_isClosing || DataAqc.loadModels.Count == 0)
            {
                return;
            }

            string policy = _viewModel.Setting.AutoSavePolicy;
            if (policy == SettingModel.AutoSaveAlwaysYes)
            {
                SaveDataAndReport_Click(this, new RoutedEventArgs());
            }
            else if (policy == SettingModel.AutoSaveAskEveryTime)
            {
                ShowAutomaticSavePrompt();
            }
        }));
    }

    private void ShowAutomaticSavePrompt()
    {
        if (_autoSavePromptOpen)
        {
            return;
        }

        _autoSavePromptOpen = true;
        var dialog = new RuntimeDataSavePromptWindow { Owner = this };
        dialog.ConfigurePrompt("自动保存数据和报告", "试验已完成，是否自动保存当前数据和试验报告？");
        dialog.Closed += (_, _) =>
        {
            _autoSavePromptOpen = false;
            if (!dialog.HasDecision)
            {
                return;
            }

            if (dialog.DontAskAgain)
            {
                _viewModel.Setting.AutoSavePolicy = dialog.ShouldSave
                    ? SettingModel.AutoSaveAlwaysYes
                    : SettingModel.AutoSaveAlwaysNo;
                _viewModel.SaveSettings();
            }

            if (dialog.ShouldSave)
            {
                _ = Dispatcher.InvokeAsync(() => SaveDataAndReport_Click(this, new RoutedEventArgs()));
            }
        };
        dialog.ShowDialog();
    }

    private static Task DelayOneSecondWithHighResolutionTimerAsync()
    {
        return Task.Run(() =>
        {
            using var timer = new HighResolutionTimer();
            timer.SetPeriod(1000);
            timer.Start();
            timer.WaitForTrigger();
        });
    }

    private static double ResolveAlgorithmSpeed(RecipeModel? recipe)
    {
        if (double.TryParse(
                SpeedVariable.CurrentValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double speedFromVariable) &&
            speedFromVariable > 0)
        {
            return speedFromVariable;
        }

        if (double.TryParse(
                SpeedVariable.CurrentValue,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out speedFromVariable) &&
            speedFromVariable > 0)
        {
            return speedFromVariable;
        }

        return recipe?.Speed > 0 ? recipe.Speed : DisplacementResamplingService.DefaultSpeed;
    }

    private void GenerateTestReport_Click(object sender, RoutedEventArgs e)
    {
        string recipeName = _viewModel.SelectedRecipe?.RecipeName ?? "NoRecipe";
        var dialog = new SaveFileDialog
        {
            Filter = "Word (*.docx)|*.docx",
            InitialDirectory = RAM.SettingModel.ExcelFolderPath,
            FileName = $"{SNModel.GetSn()}_{recipeName}_{DateTime.Now:yyyyMMddHHmmss}"
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
            var dataSnapshot = DataAqc.loadModels.ToList();
            tempImagePath = CaptureReportImageToTempFile();
            SaveTestReportDocumentToFile(
                fileName,
                tempImagePath,
                recipeName,
                SNModel.GetSn(),
                DateTime.Now,
                GetMaxForceText(dataSnapshot),
                GetMaxDistanceText(dataSnapshot),
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

    private static string GetMaxForceText(IReadOnlyList<Loadmodel> points)
    {
        return points.Count == 0
            ? "0.000"
            : points.Max(point => point.RealForce).ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string GetMaxDistanceText(IReadOnlyList<Loadmodel> points)
    {
        return points.Count == 0
            ? "0.000"
            : points.Max(point => point.RealDistance).ToString("F3", CultureInfo.InvariantCulture);
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
    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveSettingsAndApplyLanguage();
        await ApplySelectedCameraAsync();
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

    private const int SM_CYCAPTION = 4;
    private const int SM_CYFRAME = 33;
    private const int SM_CXPADDEDBORDER = 92;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint GetDpiForWindowNative(IntPtr hwnd);
}
