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
using System.Windows.Media;
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
    private enum TwoStageTrialState
    {
        None,
        WaitingForSecondStageConfirmation,
        SwitchingToSecondStage,
        SecondStageActive,
        SwitchingToFirstStage
    }
    private const string GrowlToken = "MainGrowl";
    private const int SettingsUnlockClickCount = 6;
    private const string DacuExecutableName = "DACU.exe";
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
    private readonly NewVisionProcessingService _newVisionProcessingService;
    private readonly SemaphoreSlim _twoStageTransitionLock = new(1, 1);
    private TwoStageTrialState _twoStageState;
    private bool? _twoStagePreviousAutoReset;
    private int _dataResetGeneration;
    private TwoStageConfirmationWindow? _twoStageConfirmationWindow;
    private bool _newVisionUiInitialized;
    private bool _dacuAvailable;
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
        _newVisionProcessingService = new NewVisionProcessingService();
        _viewModel.RecipeWritten += name => Dispatcher.Invoke(() => ShowSuccess($"切换配方成功：{name}"));
        DataContext = _viewModel;
        InitializeComponent();
        InitializeDacuButton();
        bool newVisionIncluded = NewVisionProcessingService.IsIncludedByPackage;
        NewVisionProcessingPanel.Visibility = newVisionIncluded ? Visibility.Visible : Visibility.Collapsed;
        UpdateNewVisionUi();
        _newVisionUiInitialized = true;
        _lastPlcConnected = string.Equals(DataAqc.plc.ConnectState, "true", StringComparison.OrdinalIgnoreCase);
        DataAqc.plc.PropertyChanged += Plc_PropertyChanged;
        DataAqc.DataCollectionEnded += OnDataCollectionEnded;
        DataAqc.ChartCleared += OnTrialDataCleared;
        DataAqc.UiBatchApplied += OnUiBatchApplied;
        Title = GetWindowTitle();
        _loadPlotController = new LoadPlotController(
            LoadPlot,
            () => _autoTrackLatestPoint,
            () => _viewModel.Setting.ShowPlotLegend,
            () => _viewModel.Setting.KeepPlotOnReset,
            11);
        PlaybackPlot.UserInputProcessor.DoubleLeftClickBenchmark(false);
        LoadPlotController.LocalizeContextMenu(PlaybackPlot);
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
        DataAqc.Refresh(Dispatcher);
        DataAqc.StartConsumers(Dispatcher);
        LoadHelpDocument();

        // 启动连接可能在超时边界之后才完成，不能使用 App 启动阶段的瞬时返回值决定失败提示。
        // 先启动正常采集链路，再在主窗口已显示的生命周期内确认当前连接并写入配方。
        bool connected = await WaitForPlcConnectionAsync();
        if (connected)
        {
            await WriteStartupRecipeAsync();
        }
        else
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                ShowError("连接失败，请检查线路！");
                ShowConnectionErrorDialog();
            });
        }

        if (HasMissingManualOffice)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                ShowWarning(ManualDocumentService.MissingOfficeMessage);
                Dialog.Show(new ManualDocumentUnavailableDialog());
            });
        }

        if (!string.IsNullOrWhiteSpace(_cameraStartupResult.FailureMessage))
        {
            _ = Dispatcher.BeginInvoke(() => ShowCameraConnectionError(_cameraStartupResult.FailureMessage));
        }

        _ = Dispatcher.BeginInvoke(InitializeCameraAfterMainWindowShownAsync);
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

    private static async Task<bool> WaitForPlcConnectionAsync()
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (IsPlcConnected())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return IsPlcConnected();
    }

    private static bool IsPlcConnected()
    {
        return DataAqc.plc?.Client is { Connected: true }
            && bool.TryParse(DataAqc.plc.ConnectState, out bool connected)
            && connected;
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
        DataAqc.ChartCleared -= OnTrialDataCleared;
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
    private async void StartTensile_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.PulseAsync("拉伸");
    }
    private async void ReleaseTensile_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.PulseAsync("拉伸释放");
        if (ReleaseTensileResetCheckBox.IsChecked == true)
        {
            await PerformDataResetAsync();
        }
    }
    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.PulseAsync("停止");
    }
    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        await PerformDataResetAsync();
    }

    private async Task PerformDataResetAsync()
    {
        TrialDataStore.CancelCaptureContinuation();
        if (_twoStageState != TwoStageTrialState.None)
        {
            if (!await SwitchToFirstStageAsync())
            {
                return;
            }
        }

        await _viewModel.PulseAsync("数据重置");
        DataAqc.RequestDataResetClear();
    }

    private async void OnTrialDataCleared()
    {
        _dataResetGeneration++;
        _twoStageConfirmationWindow?.Close();
        TrialDataStore.CancelCaptureContinuation();
        if (_twoStageState != TwoStageTrialState.None)
        {
            await SwitchToFirstStageAsync();
        }
    }
    private async void Calibration_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("传感器标零");
    private void NewVisionProcessingSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (!_newVisionUiInitialized || _newVisionProcessingService == null || NewVisionProcessingCheckBox == null)
        {
            return;
        }

        _newVisionProcessingService.SetEnabled(NewVisionProcessingCheckBox.IsChecked == true);
        UpdateNewVisionUi();
        _viewModel.SaveSettings();
    }

    private void UpdateNewVisionUi()
    {
        if (TwoStageSpeedButton == null || _newVisionProcessingService == null)
        {
            return;
        }

        TwoStageSpeedButton.Visibility = _newVisionProcessingService.IsEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
    private void TwoStageSpeed_Click(object sender, RoutedEventArgs e)
    {
        RecipeModel? source = _viewModel.SelectedRecipe;
        if (!_newVisionProcessingService.IsEnabled || source == null)
        {
            ShowWarning("请先打开新视觉处理。");
            return;
        }

        var dialog = new TwoStageSpeedDialog(source) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
        {
            return;
        }

        if (source.IsTwoStageSpeed)
        {
            if (_viewModel.UpdateRecipe(source, dialog.Result))
            {
                ShowSuccess("二段速配方已更新。");
                return;
            }

            MessageBoxResult overwrite = MessageBox.Show(
                this,
                $"配方“{dialog.Result.RecipeName}”已存在，是否覆盖？",
                "配方重名",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (overwrite == MessageBoxResult.Yes && _viewModel.ReplaceRecipe(dialog.Result.RecipeName, dialog.Result))
            {
                ShowSuccess("二段速配方已覆盖。");
                return;
            }

            ShowWarning("未覆盖已有配方，请重新编辑。");
            return;
        }

        if (!_viewModel.AddRecipe(dialog.Result.RecipeName, dialog.Result))
        {
            MessageBoxResult overwrite = MessageBox.Show(
                this,
                $"配方“{dialog.Result.RecipeName}”已存在，是否覆盖？",
                "配方重名",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes || !_viewModel.ReplaceRecipe(dialog.Result.RecipeName, dialog.Result))
            {
                ShowWarning("未覆盖已有配方。");
                return;
            }

            ShowSuccess("二段速配方已覆盖。");
            return;
        }

        ShowSuccess("二段速配方已保存。");
    }

    private async Task SwitchToSecondStageAsync()
    {
        if (_twoStageState != TwoStageTrialState.WaitingForSecondStageConfirmation)
        {
            return;
        }

        await _twoStageTransitionLock.WaitAsync();
        try
        {
            RecipeModel? recipe = _viewModel.SelectedRecipe;
            if (recipe is not { IsTwoStageSpeed: true } || recipe.SecondStageParameters == null)
            {
                _twoStageState = TwoStageTrialState.None;
                return;
            }

            _twoStageState = TwoStageTrialState.SwitchingToSecondStage;
            int resetGeneration = _dataResetGeneration;
            _twoStagePreviousAutoReset = ReleaseTensileResetCheckBox.IsChecked;
            ReleaseTensileResetCheckBox.IsChecked = false;

            if (!await _viewModel.WriteRecipeAsync(recipe.SecondStageParameters))
            {
                _twoStageState = TwoStageTrialState.SwitchingToFirstStage;
                RestoreTwoStageAutoResetSetting();
                ShowError("二段速配方写入失败，请重置数据以恢复一段速参数。");
                return;
            }

            if (resetGeneration != _dataResetGeneration || !ReferenceEquals(recipe, _viewModel.SelectedRecipe))
            {
                _twoStageState = TwoStageTrialState.SwitchingToFirstStage;
                RestoreTwoStageAutoResetSetting();
                ShowWarning("试验状态已改变，未启动二段速。");
                return;
            }

            _viewModel.DisplayRecipeStage(recipe.SecondStageParameters);
            TrialDataStore.ContinueNextCapture();
            if (!await _viewModel.TryPulseAsync("压边"))
            {
                TrialDataStore.CancelCaptureContinuation();
                _twoStageState = TwoStageTrialState.SecondStageActive;
                RestoreTwoStageAutoResetSetting();
                ShowError("压边指令发送失败，未启动二段拉伸。请检查设备并重置数据。");
                return;
            }

            if (resetGeneration != _dataResetGeneration || !ReferenceEquals(recipe, _viewModel.SelectedRecipe))
            {
                TrialDataStore.CancelCaptureContinuation();
                _twoStageState = TwoStageTrialState.SwitchingToFirstStage;
                RestoreTwoStageAutoResetSetting();
                ShowWarning("试验状态已改变，未启动二段拉伸。");
                return;
            }

            if (!await _viewModel.TryPulseAsync("拉伸"))
            {
                TrialDataStore.CancelCaptureContinuation();
                _twoStageState = TwoStageTrialState.SecondStageActive;
                RestoreTwoStageAutoResetSetting();
                ShowError("拉伸指令发送失败，请检查设备并重置数据。");
                return;
            }

            _twoStageState = TwoStageTrialState.SecondStageActive;
            RestoreTwoStageAutoResetSetting();
        }
        finally
        {
            _twoStageTransitionLock.Release();
        }
    }

    private async Task<bool> SwitchToFirstStageAsync()
    {
        await _twoStageTransitionLock.WaitAsync();
        try
        {
            if (_twoStageState == TwoStageTrialState.None)
            {
                return true;
            }

            if (_twoStageState == TwoStageTrialState.WaitingForSecondStageConfirmation)
            {
                _twoStageState = TwoStageTrialState.None;
                return true;
            }

            RecipeModel? recipe = _viewModel.SelectedRecipe;
            _twoStageState = TwoStageTrialState.SwitchingToFirstStage;
            if (recipe is { IsTwoStageSpeed: true } && recipe.FirstStageParameters != null &&
                !await _viewModel.WriteRecipeAsync(recipe.FirstStageParameters))
            {
                ShowError("一段速配方写回失败，未执行数据重置。");
                return false;
            }

            _twoStageState = TwoStageTrialState.None;
            _viewModel.DisplayRecipeStage(null);
            RestoreTwoStageAutoResetSetting();
            return true;
        }
        finally
        {
            _twoStageTransitionLock.Release();
        }
    }

    private async void ShowTwoStagePrompt()
    {
        if (_twoStageState != TwoStageTrialState.WaitingForSecondStageConfirmation)
        {
            return;
        }

        if (!_newVisionProcessingService.IsEnabled || _viewModel.SelectedRecipe is not { IsTwoStageSpeed: true })
        {
            _twoStageState = TwoStageTrialState.None;
            _viewModel.DisplayRecipeStage(null);
            RestoreTwoStageAutoResetSetting();
            return;
        }

        var dialog = new TwoStageConfirmationWindow { Owner = this };
        dialog.ConfigurePrompt("二段速试验", "拉伸流程已停止，是否开始二段速试验？");
        int resetGeneration = _dataResetGeneration;
        _twoStageConfirmationWindow = dialog;
        bool? result;
        try
        {
            result = dialog.ShowDialog();
        }
        finally
        {
            _twoStageConfirmationWindow = null;
        }

        if (resetGeneration != _dataResetGeneration || _twoStageState != TwoStageTrialState.WaitingForSecondStageConfirmation)
        {
            return;
        }
        if (result == true)
        {
            _ = SwitchToSecondStageAsync();
            return;
        }

        _twoStageState = TwoStageTrialState.None;
        RestoreTwoStageAutoResetSetting();
        await RunAutomaticSaveAsync();
    }

    private void RestoreTwoStageAutoResetSetting()
    {
        if (_twoStagePreviousAutoReset.HasValue)
        {
            ReleaseTensileResetCheckBox.IsChecked = _twoStagePreviousAutoReset.Value;
            _twoStagePreviousAutoReset = null;
        }
    }
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
        RenderPlaybackPlot(data);

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

    private void RenderPlaybackPlot(TrialDataStore.TrialPlaybackData data)
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
        PlaybackPlot.Plot.Font.Automatic();
        PlaybackPlot.Refresh();
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
                    data.Recipe,
                    _viewModel.Setting.AnnotationName?.Trim() ?? string.Empty,
                    _viewModel.Setting.AnnotationContent?.Trim() ?? string.Empty);
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

    private string BuildPlaybackBaseFileName(TrialDataStore.TrialPlaybackData data)
    {
        string recipeName = string.IsNullOrWhiteSpace(data.Recipe?.RecipeName) ? "NoRecipe" : data.Recipe.RecipeName;
        string annotationFileNamePart = GetAnnotationFileNamePart(_viewModel.Setting.AnnotationName);
        string timestamp = data.Summary.StartedAtUtc.ToLocalTime().ToString("yyyyMMddHHmmss");
        return string.IsNullOrEmpty(annotationFileNamePart)
            ? $"{data.Summary.TrialSerialNumber}_{recipeName}_回放_{timestamp}"
            : $"{data.Summary.TrialSerialNumber}_{annotationFileNamePart}_{recipeName}_回放_{timestamp}";
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

    private string CapturePlaybackPlotImageToTempFile(TrialDataStore.TrialPlaybackData data)
    {
        TrialDataStore.TrialPlaybackData? previousPlaybackData = _selectedPlaybackData;
        try
        {
            RenderPlaybackPlot(data);
            return CapturePlaybackPlotImageToTempFile();
        }
        finally
        {
            if (previousPlaybackData is null)
            {
                PlaybackPlot.Plot.Clear();
                PlaybackPlot.Refresh();
            }
            else
            {
                RenderPlaybackPlot(previousPlaybackData);
            }
        }
    }

    private async void SaveDataAndReport_Click(object sender, RoutedEventArgs e)
    {
        await SaveDataAndReportAsync(allowDatabaseFallback: false);
    }

    private async Task SaveDataAndReportAsync(bool allowDatabaseFallback, bool automatic = false)
    {
        if (automatic && IsTwoStageTransitioning)
        {
            return;
        }

        if (!EnsureTrialDataSaveAvailable())
        {
            return;
        }

        bool shouldShowTrialDataSaveNotice = RAM.IsTrial && RAM.TrialDataSaveCount is 9 or 24 or 39 or 49 or 74 or 89 or 99;
        string folderPath = RAM.SettingModel.ExcelFolderPath;
        using var waitWindow = new BackgroundStartupWaitWindow("正在保存数据及试验报告，请稍后。");
        string? tempImagePath = null;
        bool saved = false;

        try
        {
            IsEnabled = false;
            await waitWindow.ShowAsync();

            string trialSerialNumber = SNModel.GetSn();
            DateTime generatedAt = DateTime.Now;
            RecipeModel? recipe = _viewModel.SelectedRecipe;
            bool saveAlgorithmIntegratedData = AlgorithmIntegratedDataCheckBox.IsChecked == true;
            var dataSnapshot = DataAqc.loadModels.ToList();
            TrialDataStore.TrialPlaybackData? playbackFallbackData = null;
            if (dataSnapshot.Count == 0)
            {
                if (!allowDatabaseFallback)
                {
                    return;
                }

                playbackFallbackData = await Task.Run(TrialDataStore.GetMostRecentTrialPlaybackData);
                if (playbackFallbackData?.Points.Count > 0)
                {
                    dataSnapshot = playbackFallbackData.Points.ToList();
                    trialSerialNumber = playbackFallbackData.Summary.TrialSerialNumber;
                    recipe = playbackFallbackData.Recipe ?? recipe;
                }
                else
                {
                    return;
                }
            }

            string recipeName = recipe?.RecipeName ?? "NoRecipe";
            string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            string annotationName = _viewModel.Setting.AnnotationName?.Trim() ?? string.Empty;
            string annotationContent = _viewModel.Setting.AnnotationContent?.Trim() ?? string.Empty;
            string baseFileName = BuildBaseFileName(trialSerialNumber, recipeName, timestamp, annotationName);
            Directory.CreateDirectory(folderPath);
            string excelPath = Path.Combine(folderPath, $"{baseFileName}.xlsx");
            string algorithmIntegratedDataPath = Path.Combine(folderPath, $"{baseFileName}_算法整合数据.xlsx");
            string reportPath = Path.Combine(folderPath, $"{baseFileName}.docx");
            string maxForce = GetMaxForceText(dataSnapshot);
            string validDistance = GetMaxDistanceText(dataSnapshot);
            double algorithmDisplacementStep = DisplacementResamplingService.GetDisplacementStep(
                ResolveAlgorithmSpeed(recipe));

            IDisposable? currentPlotScope = null;
            try
            {
                if (playbackFallbackData is null)
                {
                    currentPlotScope = BeginCurrentTrialPlotScope();
                    tempImagePath = CaptureReportImageToTempFile();
                }
                else
                {
                    tempImagePath = CapturePlaybackPlotImageToTempFile(playbackFallbackData);
                }

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
                        recipe,
                        annotationName,
                        annotationContent);
                });
            }
            finally
            {
                currentPlotScope?.Dispose();
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

    private async void OnDataCollectionEnded()
    {
        if (_isClosing)
        {
            return;
        }

        if (DataAqc.CurrentCollectionWasReset &&
            (_twoStageState != TwoStageTrialState.None ||
             (_newVisionProcessingService.IsEnabled && _viewModel.SelectedRecipe is { IsTwoStageSpeed: true })))
        {
            return;
        }

        if (_twoStageState == TwoStageTrialState.None &&
            _newVisionProcessingService.IsEnabled &&
            _viewModel.SelectedRecipe is { IsTwoStageSpeed: true })
        {
            _twoStageState = TwoStageTrialState.WaitingForSecondStageConfirmation;
            await DelayOneSecondWithHighResolutionTimerAsync();
            if (_twoStageState == TwoStageTrialState.WaitingForSecondStageConfirmation)
            {
                ShowTwoStagePrompt();
            }
            return;
        }

        await DelayOneSecondWithHighResolutionTimerAsync();
        await RunAutomaticSaveAsync();
    }

    private bool IsTwoStageTransitioning =>
        _twoStageState is TwoStageTrialState.WaitingForSecondStageConfirmation
            or TwoStageTrialState.SwitchingToSecondStage
            or TwoStageTrialState.SwitchingToFirstStage;

    private async Task RunAutomaticSaveAsync()
    {
        if (_isClosing || IsTwoStageTransitioning || DataAqc.loadModels.Count == 0)
        {
            return;
        }

        string policy = _viewModel.Setting.AutoSavePolicy;
        if (policy == SettingModel.AutoSaveAlwaysYes)
        {
            await SaveDataAndReportAsync(allowDatabaseFallback: true, automatic: true);
        }
        else if (policy == SettingModel.AutoSaveAskEveryTime)
        {
            await ShowAutomaticSavePromptAsync();
        }
    }

    private async Task ShowAutomaticSavePromptAsync()
    {
        if (_autoSavePromptOpen || IsTwoStageTransitioning)
        {
            return;
        }

        _autoSavePromptOpen = true;
        var dialog = new RuntimeDataSavePromptWindow { Owner = this };
        dialog.ConfigurePrompt("自动保存数据和报告", "试验已完成，是否自动保存当前数据和试验报告？");
        try
        {
            dialog.ShowDialog();
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
                await SaveDataAndReportAsync(allowDatabaseFallback: true, automatic: true);
            }
        }
        finally
        {
            _autoSavePromptOpen = false;
        }
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
            FileName = BuildBaseFileName(
                SNModel.GetSn(),
                recipeName,
                DateTime.Now.ToString("yyyyMMddHHmmss"),
                _viewModel.Setting.AnnotationName)
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
                _viewModel.SelectedRecipe,
                _viewModel.Setting.AnnotationName?.Trim() ?? string.Empty,
                _viewModel.Setting.AnnotationContent?.Trim() ?? string.Empty);
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
        string tempImagePath = Path.Combine(Path.GetTempPath(), $"TensileReport_{Guid.NewGuid():N}.png");
        LoadPlot.Plot.SavePng(tempImagePath, 1200, 700);
        return tempImagePath;
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
        RecipeModel? recipe,
        string annotationName,
        string annotationContent)
    {
        TestReportService.Save(
            fileName,
            imagePath,
            recipeName,
            trialSerialNumber,
            generatedAt,
            maxForce,
            validDistance,
            recipe,
            annotationName,
            annotationContent);
    }

    private static string BuildBaseFileName(
        string trialSerialNumber,
        string recipeName,
        string timestamp,
        string? annotationName)
    {
        string annotationFileNamePart = GetAnnotationFileNamePart(annotationName);
        return string.IsNullOrEmpty(annotationFileNamePart)
            ? $"{trialSerialNumber}_{recipeName}_{timestamp}"
            : $"{trialSerialNumber}_{annotationFileNamePart}_{recipeName}_{timestamp}";
    }

    private static string GetAnnotationFileNamePart(string? annotationName)
    {
        string value = annotationName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidCharacter, '_');
        }

        return value;
    }

    private void EditAnnotation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AnnotationDialog(
            _viewModel.Setting.AnnotationName,
            _viewModel.Setting.AnnotationContent);
        dialog.Confirmed += (_, args) =>
        {
            _viewModel.Setting.AnnotationName = args.Name;
            _viewModel.Setting.AnnotationContent = args.Content;
            _viewModel.SaveSettings();
            _viewModel.RefreshAnnotationInfo();
        };
        Dialog.Show(dialog);
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
                args.CloseDialog = false;
                ShowWarning("配方名称不能为空");
                return;
            }

            if (_viewModel.AddRecipe(name, args.TemplateRecipe))
            {
                ShowSuccess("添加试验成功");
                return;
            }

            MessageBoxResult overwrite = MessageBox.Show(
                this,
                $"配方“{name}”已存在，是否覆盖？",
                "配方重名",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (overwrite == MessageBoxResult.Yes && _viewModel.ReplaceRecipe(name, args.TemplateRecipe))
            {
                args.CloseDialog = true;
                ShowSuccess("配方已覆盖");
                return;
            }

            args.CloseDialog = false;
            ShowWarning("未覆盖已有配方，请修改名称或选择覆盖。");
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
