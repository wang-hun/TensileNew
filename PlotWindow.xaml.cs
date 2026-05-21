using System.Windows;
using System.Windows.Threading;
using TensileNeW.Models;

namespace TensileNeW;

public partial class PlotWindow : Window
{
    private readonly Func<bool> _autoPlayEnabled;
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<double> _xs = [];
    private readonly List<double> _ys = [];
    private ScottPlot.Plottables.Scatter? _scatter;
    private int _pointCount;
    private bool _initialized;
    private static readonly ScottPlot.Color SanaePlotBackgroundColor = ScottPlot.Color.FromHex("#D4D5CF");
    private static readonly ScottPlot.Color SanaePlotLineColor = ScottPlot.Color.FromHex("#5CAB8C");

    public PlotWindow(Func<bool> autoPlayEnabled)
    {
        _autoPlayEnabled = autoPlayEnabled;
        InitializeComponent();
        Owner = Application.Current?.MainWindow;
        Topmost = true;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            RefreshPlot();
        };
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
            };
            RefreshPlot();
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
            _scatter.Color = SanaePlotLineColor;
        }

        ApplyPlotLabels();
        if (_autoPlayEnabled() && _xs.Count > 0)
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
        PlotHost.Plot.XLabel("位移", 30);
        PlotHost.Plot.YLabel("力", 30);
        PlotHost.Plot.Axes.Bottom.TickLabelStyle.FontSize = 44;
        PlotHost.Plot.Axes.Left.TickLabelStyle.FontSize = 44;
        PlotHost.Plot.Axes.Bottom.TickGenerator.MaxTickCount = 6;
        PlotHost.Plot.Axes.Left.TickGenerator.MaxTickCount = 6;
        PlotHost.Plot.Font.Automatic();
    }

    private void ApplyPlotStyle()
    {
        PlotHost.Plot.DataBackground.Color = SanaePlotBackgroundColor;
        if (_scatter != null)
        {
            _scatter.Color = SanaePlotLineColor;
        }
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
}
