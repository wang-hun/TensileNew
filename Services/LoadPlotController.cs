using ScottPlot;
using ScottPlot.WPF;
using TensileNeW.Models;

namespace TensileNeW.Services;

public sealed class LoadPlotController
{
    private const bool AutoScrollXAxisEnabled = false;
    private const float GridLineOpacity = 0.25f;
    private static readonly Color SanaePlotBackgroundColor = Color.FromHex("#E1E2DC");
    private static readonly Color SanaePlotGridLineColor = Color.FromHex("#000000");
    private static readonly Color[] PlotLineColors =
    [
        Color.FromHex("#003A8C"),
        Color.FromHex("#237804"),
        Color.FromHex("#AD4E00"),
        Color.FromHex("#9E1068"),
        Color.FromHex("#391085"),
        Color.FromHex("#006D75"),
        Color.FromHex("#A8071A"),
        Color.FromHex("#5B8C00"),
        Color.FromHex("#1D39C4"),
        Color.FromHex("#AD6800")
    ];

    private readonly WpfPlot _plotHost;
    private readonly Func<bool> _autoTrackLatestPoint;
    private readonly Func<bool> _showLegend;
    private readonly Func<bool> _keepPlotOnReset;
    private readonly int _tickLabelFontSize;
    private readonly Queue<CurveSeries> _seriesQueue = new();
    private readonly Queue<string> _visibleTrialSerialNumbers = new();
    private readonly List<CurveSeries> _temporarilyHiddenSeries = [];
    private readonly Color _defaultFigureBackgroundColor;
    private readonly Color _defaultPlotBackgroundColor;
    private readonly Color _defaultPlotMajorGridLineColor;
    private CurveSeries? _currentSeries;
    private int _pointCount;
    private int _nextColorIndex;
    private bool _initialized;

    public LoadPlotController(WpfPlot plotHost, Func<bool> autoTrackLatestPoint, Func<bool> showLegend, Func<bool> keepPlotOnReset, int tickLabelFontSize)
    {
        _plotHost = plotHost;
        _autoTrackLatestPoint = autoTrackLatestPoint;
        _showLegend = showLegend;
        _keepPlotOnReset = keepPlotOnReset;
        _tickLabelFontSize = tickLabelFontSize;
        _defaultFigureBackgroundColor = _plotHost.Plot.FigureBackground.Color;
        _defaultPlotBackgroundColor = _plotHost.Plot.DataBackground.Color;
        _defaultPlotMajorGridLineColor = _plotHost.Plot.Grid.MajorLineColor;
    }

    public void Initialize(Action? configureMenu = null)
    {
        if (_initialized)
        {
            return;
        }

        ApplyLabels();
        _plotHost.UserInputProcessor.DoubleLeftClickBenchmark(false);
        configureMenu?.Invoke();
        _plotHost.Refresh();
        _initialized = true;
    }

    public void Reset()
    {
        if (_keepPlotOnReset())
        {
            _pointCount = 0;
            _currentSeries = null;
            ApplyLabels();
            _plotHost.Refresh();
            return;
        }

        _plotHost.Plot.Clear();
        _seriesQueue.Clear();
        _visibleTrialSerialNumbers.Clear();
        _temporarilyHiddenSeries.Clear();
        _currentSeries = null;
        _pointCount = 0;
        _nextColorIndex = 0;
        ApplyLabels();
        _plotHost.Refresh();
    }

    public void Refresh()
    {
        if (!_initialized)
        {
            Initialize();
        }

        var items = DataAqc.loadModels;
        if (items == null)
        {
            return;
        }

        var limits = _plotHost.Plot.Axes.GetLimits();
        if (items.Count < _pointCount)
        {
            Reset();
            limits = _plotHost.Plot.Axes.GetLimits();
        }

        while (_pointCount < items.Count)
        {
            CurveSeries series = _currentSeries ?? CreateSeries(SNModel.GetSn());
            var item = items[_pointCount];
            series.Xs.Add(item.RealDistance);
            series.Ys.Add(item.RealForce);
            if (series.Scatter == null)
            {
                series.Scatter = _plotHost.Plot.Add.Scatter(series.Xs, series.Ys);
                series.Scatter.Smooth = true;
                series.Scatter.LegendText = series.TrialSerialNumber;
                series.Scatter.Color = series.LineColor;
                series.Scatter.MarkerSize = 0;
            }

            _pointCount++;
        }

        ApplyLabels();
        if (AutoScrollXAxisEnabled && _autoTrackLatestPoint() && _currentSeries is { Xs.Count: > 0 } current)
        {
            double xSpan = limits.Right - limits.Left;
            if (xSpan <= 0)
            {
                xSpan = Math.Max(1, current.Xs.Max() - current.Xs.Min());
            }

            double latestX = current.Xs[^1];
            _plotHost.Plot.Axes.SetLimits(latestX - xSpan, latestX, limits.Bottom, limits.Top);
        }

        _plotHost.Refresh();
    }

    public void ApplyCurrentTheme()
    {
        ApplyLabels();
        _plotHost.Refresh();
    }

    public void AutoScaleWhileCollecting()
    {
        if (!_autoTrackLatestPoint() || !IsDataCollecting() || _currentSeries?.Scatter == null || _currentSeries.Xs.Count == 0)
        {
            return;
        }

        AutoScale();
    }

    public void AutoScale()
    {
        _plotHost.Plot.Axes.AutoScale();
        _plotHost.Refresh();
    }

    public void HideNonCurrentCurves()
    {
        _temporarilyHiddenSeries.Clear();
        foreach (CurveSeries series in _seriesQueue)
        {
            if (series == _currentSeries || series.Scatter == null || !series.Scatter.IsVisible)
            {
                continue;
            }

            series.Scatter.IsVisible = false;
            _temporarilyHiddenSeries.Add(series);
        }

        _plotHost.Refresh();
    }

    public void RestoreHiddenCurves()
    {
        foreach (CurveSeries series in _temporarilyHiddenSeries)
        {
            if (series.Scatter != null)
            {
                series.Scatter.IsVisible = true;
            }
        }

        _temporarilyHiddenSeries.Clear();
        _plotHost.Refresh();
    }

    public void LocalizeContextMenu(Action? openInNewWindow = null)
    {
        var menu = _plotHost.Menu;
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
                    if (openInNewWindow == null)
                    {
                        items.RemoveAt(i);
                        i--;
                    }
                    else
                    {
                        if (!isEn) item.Label = "新窗口打开";
                        item.OnInvoke = _ => openInNewWindow();
                        items[i] = item;
                    }
                    break;
            }
        }
    }

    private void ApplyLabels()
    {
        ApplyStyle();
        _plotHost.Plot.Title("力位移数据", 15);
        _plotHost.Plot.XLabel("位移（mm）", 15);
        _plotHost.Plot.YLabel("力（KN）", 15);
        _plotHost.Plot.Axes.Bottom.TickLabelStyle.FontSize = _tickLabelFontSize;
        _plotHost.Plot.Axes.Left.TickLabelStyle.FontSize = _tickLabelFontSize;
        _plotHost.Plot.Axes.Bottom.TickGenerator.MaxTickCount = 6;
        _plotHost.Plot.Axes.Left.TickGenerator.MaxTickCount = 6;
        _plotHost.Plot.Font.Automatic();
        ApplyLegendVisibility();
    }

    private void ApplyLegendVisibility()
    {
        if (_showLegend())
        {
            _plotHost.Plot.ShowLegend();
            return;
        }

        _plotHost.Plot.Legend.IsVisible = false;
    }

    private void ApplyStyle()
    {
        bool useSanae = ThemeManager.UsesWarningStyle(ThemeManager.CurrentScheme);

        _plotHost.Plot.FigureBackground.Color = useSanae ? SanaePlotBackgroundColor : _defaultFigureBackgroundColor;
        _plotHost.Plot.DataBackground.Color = useSanae ? SanaePlotBackgroundColor : _defaultPlotBackgroundColor;
        ApplyGridLineColor(WithGridLineOpacity(useSanae ? SanaePlotGridLineColor : _defaultPlotMajorGridLineColor));
        foreach (CurveSeries series in _seriesQueue)
        {
            if (series.Scatter != null)
            {
                series.Scatter.Color = series.LineColor;
            }
        }
    }

    private static Color WithGridLineOpacity(Color color)
    {
        return color.WithOpacity(GridLineOpacity);
    }

    private void ApplyGridLineColor(Color color)
    {
        _plotHost.Plot.Grid.MajorLineColor = color;
        _plotHost.Plot.Grid.MinorLineColor = color;
        _plotHost.Plot.Grid.XAxisStyle.MajorLineStyle.Color = color;
        _plotHost.Plot.Grid.XAxisStyle.MinorLineStyle.Color = color;
        _plotHost.Plot.Grid.YAxisStyle.MajorLineStyle.Color = color;
        _plotHost.Plot.Grid.YAxisStyle.MinorLineStyle.Color = color;
    }

    private CurveSeries CreateSeries(string trialSerialNumber)
    {
        var series = new CurveSeries(
            trialSerialNumber,
            PlotLineColors[_nextColorIndex % PlotLineColors.Length]);
        _nextColorIndex++;
        _seriesQueue.Enqueue(series);
        _visibleTrialSerialNumbers.Enqueue(trialSerialNumber);
        _currentSeries = series;

        while (_seriesQueue.Count > 10)
        {
            CurveSeries removed = _seriesQueue.Dequeue();
            _visibleTrialSerialNumbers.Dequeue();
            if (removed.Scatter != null)
            {
                _plotHost.Plot.Remove(removed.Scatter);
            }
        }

        return series;
    }

    private static bool IsDataCollecting()
    {
        DataAqc.EnsureInitialized();
        var variable = DataAqc.PLCVariables.First(t => t.Name == "数据采集标志");
        return bool.TryParse(variable.CurrentValue, out bool value) && value;
    }

    private sealed class CurveSeries(string trialSerialNumber, Color lineColor)
    {
        public string TrialSerialNumber { get; } = trialSerialNumber;
        public Color LineColor { get; } = lineColor;
        public List<double> Xs { get; } = [];
        public List<double> Ys { get; } = [];
        public ScottPlot.Plottables.Scatter? Scatter { get; set; }
    }
}
