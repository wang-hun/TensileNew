using System.Windows;
using System.Windows.Threading;
using TensileNeW.Models;

namespace TensileNeW;

public partial class PlotWindow : Window
{
    private const bool AutoScrollXAxisEnabled = false;
    private readonly Func<bool> _autoPlayEnabled;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _autoscaleTimer;
    private readonly List<double> _xs = [];
    private readonly List<double> _ys = [];
    private ScottPlot.Plottables.Scatter? _scatter;
    private int _pointCount;
    private bool _initialized;
    private static readonly ScottPlot.Color SanaePlotBackgroundColor = ScottPlot.Color.FromHex("#E1E2DC");
    private static readonly ScottPlot.Color SanaePlotLineColor = ScottPlot.Color.FromHex("#5CAB8C");
    private static readonly ScottPlot.Color SanaePlotGridLineColor = ScottPlot.Color.FromHex("#000000");
    private ScottPlot.Color _defaultPlotBackgroundColor;
    private ScottPlot.Color _defaultPlotMajorGridLineColor;
    private ScottPlot.Color? _defaultScatterColor;

    public PlotWindow(Func<bool> autoPlayEnabled)
    {
        _autoPlayEnabled = autoPlayEnabled;
        InitializeComponent();
        _defaultPlotBackgroundColor = PlotHost.Plot.DataBackground.Color;
        _defaultPlotMajorGridLineColor = PlotHost.Plot.Grid.MajorLineColor;
        Owner = Application.Current?.MainWindow;
        Topmost = true;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            RefreshPlot();
        };
        _autoscaleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _autoscaleTimer.Tick += (_, _) => AutoScalePlotWhileCollecting();
        Loaded += (_, _) =>
        {
            InitializePlot();
            LocalizePlotContextMenu();
            DataAqc.LoadDataChanged += OnLoadDataChanged;
            DataAqc.ChartCleared += OnChartCleared;
            Closed += (_, _) =>
            {
                DataAqc.LoadDataChanged -= OnLoadDataChanged;
                DataAqc.ChartCleared -= OnChartCleared;
                _autoscaleTimer.Stop();
            };
            RefreshPlot();
            _autoscaleTimer.Start();
        };
    }

    private void OnLoadDataChanged(Loadmodel _) => Dispatcher.Invoke(RequestRefresh);

    private void OnChartCleared() => Dispatcher.Invoke(ResetPlot);

    private void RequestRefresh()
    {
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }
    }

    private void InitializePlot()
    {
        if (_initialized)
        {
            return;
        }

        ApplyPlotLabels();
        _initialized = true;
    }

    private void ResetPlot()
    {
        PlotHost.Plot.Clear();
        _xs.Clear();
        _ys.Clear();
        _scatter = null;
        _pointCount = 0;
        ApplyPlotLabels();
        PlotHost.Refresh();
    }

    private void RefreshPlot()
    {
        if (!_initialized)
        {
            InitializePlot();
        }

        var items = DataAqc.loadModels;
        var limits = PlotHost.Plot.Axes.GetLimits();
        if (items.Count < _pointCount)
        {
            ResetPlot();
            limits = PlotHost.Plot.Axes.GetLimits();
        }

        while (_pointCount < items.Count)
        {
            var item = items[_pointCount];
            _xs.Add(item.RealDistance);
            _ys.Add(item.RealForce);
            _pointCount++;
        }

        if (_scatter == null && _xs.Count > 0)
        {
            _scatter = PlotHost.Plot.Add.Scatter(_xs, _ys);
            _scatter.Smooth = true;
            _defaultScatterColor = _scatter.Color;
        }

        ApplyPlotLabels();
        if (AutoScrollXAxisEnabled && _autoPlayEnabled() && _xs.Count > 0)
        {
            double xSpan = limits.Right - limits.Left;
            if (xSpan <= 0)
            {
                xSpan = Math.Max(1, _xs.Max() - _xs.Min());
            }

            double latestX = _xs[^1];
            PlotHost.Plot.Axes.SetLimits(latestX - xSpan, latestX, limits.Bottom, limits.Top);
        }

        PlotHost.Refresh();
    }

    private void ApplyPlotLabels()
    {
        ApplyPlotStyle();
        PlotHost.Plot.Title("力位移数据", 30);
        PlotHost.Plot.XLabel("位移（mm）", 30);
        PlotHost.Plot.YLabel("力（KN）", 30);
        PlotHost.Plot.Axes.Bottom.TickLabelStyle.FontSize = 44;
        PlotHost.Plot.Axes.Left.TickLabelStyle.FontSize = 44;
        PlotHost.Plot.Axes.Bottom.TickGenerator.MaxTickCount = 6;
        PlotHost.Plot.Axes.Left.TickGenerator.MaxTickCount = 6;
        PlotHost.Plot.Font.Automatic();
    }

    private void ApplyPlotStyle()
    {
        bool useSanae = string.Equals(ThemeManager.CurrentScheme.Name, ThemeManager.DefaultSchemeName, StringComparison.Ordinal);

        PlotHost.Plot.DataBackground.Color = useSanae ? SanaePlotBackgroundColor : _defaultPlotBackgroundColor;
        PlotHost.Plot.Grid.MajorLineColor = useSanae ? SanaePlotGridLineColor : _defaultPlotMajorGridLineColor;
        PlotHost.Plot.Grid.MinorLineColor = useSanae ? SanaePlotGridLineColor : _defaultPlotMajorGridLineColor;
        if (_scatter != null)
        {
            _scatter.Color = useSanae
                ? SanaePlotLineColor
                : _defaultScatterColor ?? _scatter.Color;
        }
    }

    public void ApplyCurrentTheme()
    {
        ApplyPlotLabels();
        PlotHost.Refresh();
    }

    private void AutoScalePlotWhileCollecting()
    {
        if (!_autoPlayEnabled() || !IsDataCollecting() || _scatter == null || _xs.Count == 0)
        {
            return;
        }

        PlotHost.Plot.Axes.AutoScale();
        PlotHost.Refresh();
    }

    private void LocalizePlotContextMenu()
    {
        var menu = PlotHost.Menu;
        if (menu == null)
        {
            return;
        }

        bool isEn = string.Equals(RAM.SettingModel.Language, "EN", StringComparison.OrdinalIgnoreCase);
        var items = menu.ContextMenuItems;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            switch (item.Label)
            {
                case "Save Image":
                    if (!isEn) item.Label = "保存图片";
                    items[i] = item;
                    break;
                case "Copy to Clipboard":
                    if (!isEn) item.Label = "复制到剪贴板";
                    items[i] = item;
                    break;
                case "Autoscale":
                    if (!isEn) item.Label = "自动缩放";
                    items[i] = item;
                    break;
                case "Open in New Window":
                    items.RemoveAt(i);
                    i--;
                    break;
            }
        }
    }

    private static bool IsDataCollecting()
    {
        DataAqc.EnsureInitialized();
        var variable = DataAqc.PLCVariables.First(t => t.Name == "数据采集标志");
        return bool.TryParse(variable.CurrentValue, out bool value) && value;
    }
}
