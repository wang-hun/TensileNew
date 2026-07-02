using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using TensileNeW.Services;

namespace TensileNeW;

public partial class CurveFilterWindow : Window
{
    public ObservableCollection<CurveFilterRow> Rows { get; }

    public CurveFilterWindow(IEnumerable<LoadPlotController.CurveFilterEntry> entries)
    {
        InitializeComponent();
        Rows = new ObservableCollection<CurveFilterRow>(
            entries.Select(entry => new CurveFilterRow(entry)));
        DataContext = this;
    }

    public IReadOnlyList<LoadPlotController.CurveFilterSelection> GetSelections()
    {
        return Rows
            .Select(row => new LoadPlotController.CurveFilterSelection(row.CurveId, row.IsChecked))
            .ToList();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeTitleBarHelper.ApplyTheme(this);
    }
}

public sealed partial class CurveFilterRow : ObservableObject
{
    [ObservableProperty]
    private bool isChecked;

    public CurveFilterRow(LoadPlotController.CurveFilterEntry entry)
    {
        CurveId = entry.CurveId;
        TrialSerialNumber = entry.TrialSerialNumber;
        StartedAtText = entry.StartedAtUtc.HasValue
            ? entry.StartedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : string.Empty;
        isChecked = entry.IsVisible;
    }

    public int CurveId { get; }

    public string TrialSerialNumber { get; }

    public string StartedAtText { get; }
}
