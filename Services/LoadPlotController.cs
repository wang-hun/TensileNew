using ScottPlot;
using ScottPlot.WPF;
using TensileNeW.Models;

namespace TensileNeW.Services;

public sealed class LoadPlotController
{
    private const bool AutoScrollXAxisEnabled = false;
    private static readonly Color SanaePlotBackgroundColor = Color.FromHex("#E1E2DC");
    private static readonly Color SanaePlotLineColor = Color.FromHex("#101010");
    private static readonly Color SanaePlotGridLineColor = Color.FromHex("#000000");

    private readonly WpfPlot _plotHost;
    private readonly Func<bool> _autoTrackLatestPoint;
    private readonly int _tickLabelFontSize;
    private readonly List<double> _xs = [];
    private readonly List<double> _ys = [];
    private readonly Color _defaultPlotBackgroundColor;
    private readonly Color _defaultPlotMajorGridLineColor;
    private ScottPlot.Plottables.Scatter? _scatter;
    private Color? _defaultScatterColor;
    private int _pointCount;
    private bool _initialized;

    public LoadPlotController(WpfPlot plotHost, Func<bool> autoTrackLatestPoint, int tickLabelFontSize)
    {
        _plotHost = plotHost;
        _autoTrackLatestPoint = autoTrackLatestPoint;
        _tickLabelFontSize = tickLabelFontSize;
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
        configureMenu?.Invoke();
        _plotHost.Refresh();
        _initialized = true;
    }

    public void Reset()
    {
        _plotHost.Plot.Clear();
        _xs.Clear();
        _ys.Clear();
        _scatter = null;
        _pointCount = 0;
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
            var item = items[_pointCount];
            _xs.Add(item.RealDistance);
            _ys.Add(item.RealForce);
            _pointCount++;
        }

        if (_scatter == null && _xs.Count > 0)
        {
            _scatter = _plotHost.Plot.Add.Scatter(_xs, _ys);
            _scatter.Smooth = true;
            _scatter.LegendText = SNModel.GetSn();
            _defaultScatterColor = _scatter.Color;
            _plotHost.Plot.ShowLegend();
        }

        ApplyLabels();
        if (AutoScrollXAxisEnabled && _autoTrackLatestPoint() && _xs.Count > 0)
        {
            double xSpan = limits.Right - limits.Left;
            if (xSpan <= 0)
            {
                xSpan = Math.Max(1, _xs.Max() - _xs.Min());
            }

            double latestX = _xs[^1];
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
        if (!_autoTrackLatestPoint() || !IsDataCollecting() || _scatter == null || _xs.Count == 0)
        {
            return;
        }

        _plotHost.Plot.Axes.AutoScale();
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
        _plotHost.Plot.Title("力位移数据", 30);
        _plotHost.Plot.XLabel("位移（mm）", 30);
        _plotHost.Plot.YLabel("力（KN）", 30);
        _plotHost.Plot.Axes.Bottom.TickLabelStyle.FontSize = _tickLabelFontSize;
        _plotHost.Plot.Axes.Left.TickLabelStyle.FontSize = _tickLabelFontSize;
        _plotHost.Plot.Axes.Bottom.TickGenerator.MaxTickCount = 6;
        _plotHost.Plot.Axes.Left.TickGenerator.MaxTickCount = 6;
        _plotHost.Plot.Font.Automatic();
    }

    private void ApplyStyle()
    {
        bool useSanae = string.Equals(ThemeManager.CurrentScheme.Name, ThemeManager.DefaultSchemeName, StringComparison.Ordinal);

        _plotHost.Plot.DataBackground.Color = useSanae ? SanaePlotBackgroundColor : _defaultPlotBackgroundColor;
        _plotHost.Plot.Grid.MajorLineColor = useSanae ? SanaePlotGridLineColor : _defaultPlotMajorGridLineColor;
        _plotHost.Plot.Grid.MinorLineColor = useSanae ? SanaePlotGridLineColor : _defaultPlotMajorGridLineColor;
        if (_scatter != null)
        {
            _scatter.Color = useSanae
                ? SanaePlotLineColor
                : _defaultScatterColor ?? _scatter.Color;
        }
    }

    private static bool IsDataCollecting()
    {
        DataAqc.EnsureInitialized();
        var variable = DataAqc.PLCVariables.First(t => t.Name == "数据采集标志");
        return bool.TryParse(variable.CurrentValue, out bool value) && value;
    }
}
