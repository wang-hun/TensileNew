using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using TensileNeW.Models;

namespace TensileNeW;

public partial class LoadDataWindow : Window
{
    private readonly DispatcherTimer _scrollTimer;
    private int _pendingScrollIndex = -1;

    public LoadDataWindow()
    {
        InitializeComponent();
        _scrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _scrollTimer.Tick += (_, _) =>
        {
            _scrollTimer.Stop();
            ScrollToLatestRow();
        };
        DataAqc.loadModels.ListChanged += LoadModels_ListChanged;
        Closed += (_, _) => DataAqc.loadModels.ListChanged -= LoadModels_ListChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeTitleBarHelper.ApplyTheme(this);
    }

    private void LoadModels_ListChanged(object? sender, ListChangedEventArgs e)
    {
        if (LoadDataAutoScrollCheckBox.IsChecked != true ||
            e.ListChangedType != ListChangedType.ItemAdded ||
            e.NewIndex < 0)
        {
            return;
        }

        _pendingScrollIndex = e.NewIndex;
        if (!_scrollTimer.IsEnabled)
        {
            _scrollTimer.Start();
        }
    }

    private void ScrollToLatestRow()
    {
        if (_pendingScrollIndex < 0 || _pendingScrollIndex >= DataAqc.loadModels.Count)
        {
            return;
        }

        LoadDataGrid.ScrollIntoView(DataAqc.loadModels[_pendingScrollIndex]);
        _pendingScrollIndex = -1;
    }
}
