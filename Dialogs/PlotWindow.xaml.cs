using System.Windows;
using System.Windows.Threading;
using TensileNeW.Models;
using TensileNeW.Services;

namespace TensileNeW;

public partial class PlotWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _autoscaleTimer;
    private readonly LoadPlotController _plotController;

    public PlotWindow(Func<bool> autoPlayEnabled)
    {
        InitializeComponent();
        _plotController = new LoadPlotController(PlotHost, autoPlayEnabled, 44);
        Owner = Application.Current?.MainWindow;
        Topmost = true;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            _plotController.Refresh();
        };

        _autoscaleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _autoscaleTimer.Tick += (_, _) => _plotController.AutoScaleWhileCollecting();

        Loaded += (_, _) =>
        {
            _plotController.Initialize(() => _plotController.LocalizeContextMenu());
            DataAqc.LoadDataChanged += OnLoadDataChanged;
            DataAqc.ChartCleared += OnChartCleared;
            Closed += (_, _) =>
            {
                DataAqc.LoadDataChanged -= OnLoadDataChanged;
                DataAqc.ChartCleared -= OnChartCleared;
                _autoscaleTimer.Stop();
            };
            _plotController.Refresh();
            _autoscaleTimer.Start();
        };
    }

    public void ApplyCurrentTheme() => _plotController.ApplyCurrentTheme();

    private void OnLoadDataChanged(Loadmodel _) => Dispatcher.Invoke(RequestRefresh);

    private void OnChartCleared() => Dispatcher.Invoke(_plotController.Reset);

    private void RequestRefresh()
    {
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }
    }
}
