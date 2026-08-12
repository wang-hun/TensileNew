using System.Windows;
using System.Windows.Threading;
using TensileNeW.Models;
using TensileNeW.Services;
using Dialog = HandyControl.Controls.Dialog;

namespace TensileNeW;

public partial class PlotWindow : Window
{
    private readonly LoadPlotController _plotController;

    public PlotWindow(Func<bool> autoPlayEnabled, Func<bool> showLegend, Func<bool> keepPlotOnReset)
    {
        InitializeComponent();
        _plotController = new LoadPlotController(PlotHost, autoPlayEnabled, showLegend, keepPlotOnReset, 22);
        Owner = Application.Current?.MainWindow;
        Topmost = true;

        Loaded += (_, _) =>
        {
            _plotController.Initialize(() => _plotController.LocalizeContextMenu(
                filterCurves: ShowCurveFilterDialog,
                clearCurrentPlot: ShowClearPlotConfirmDialog));
            DataAqc.LoadDataBatchChanged += OnLoadDataBatchChanged;
            DataAqc.ChartCleared += OnChartCleared;
            Closed += (_, _) =>
            {
                DataAqc.LoadDataBatchChanged -= OnLoadDataBatchChanged;
                DataAqc.ChartCleared -= OnChartCleared;
            };
            _plotController.Refresh();
        };
    }

    public void ApplyCurrentTheme() => _plotController.ApplyCurrentTheme();

    public void HideNonCurrentCurves() => _plotController.HideNonCurrentCurves();

    public void RestoreHiddenCurves() => _plotController.RestoreHiddenCurves();

    public void AutoScale() => _plotController.AutoScale();

    private void OnLoadDataBatchChanged(IReadOnlyList<Loadmodel> _) => _plotController.Refresh(autoScale: true);

    private void OnChartCleared() => Dispatcher.Invoke(_plotController.Reset);

    private void ShowCurveFilterDialog()
    {
        IReadOnlyList<LoadPlotController.CurveFilterEntry> entries = _plotController.GetCurveFilterEntries();
        if (entries.Count == 0)
        {
            return;
        }

        var dialog = new CurveFilterWindow(entries)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _plotController.ApplyCurveFilter(dialog.GetSelections());
        }
    }

    private void ShowClearPlotConfirmDialog()
    {
        if (!_plotController.HasCurves)
        {
            return;
        }

        var dialog = new ClearPlotConfirmDialog();
        dialog.Confirmed += (_, _) => _plotController.ClearCurrentPlotCurves();
        Dialog.Show(dialog);
    }

}
