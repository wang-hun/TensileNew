using NLog;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TensileNeW.Models;
using Growl = HandyControl.Controls.Growl;
using MessageBox = HandyControl.Controls.MessageBox;

namespace TensileNeW;

public partial class MainWindow : Window
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly bool _connectedAtStartup;
    private readonly MainViewModel _viewModel;
    private bool _plotInitialized;

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

    public static PLCVariable StartPressCoil => FindVariable("压边线圈");
    public static PLCVariable ReleasePressCoil => FindVariable("压边释放线圈");
    public static PLCVariable StartTensileCoil => FindVariable("拉伸线圈");
    public static PLCVariable ReleaseTensileCoil => FindVariable("拉伸释放线圈");
    public static PLCVariable TanliaoVariable => FindVariable("弹料");
    public static PLCVariable CalibrationStateVariable => FindVariable("传感器标零状态");

    public MainWindow(bool connectedAtStartup)
    {
        _connectedAtStartup = connectedAtStartup;
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Logger.Info("启动程序");
        InitializePlot();
        DataAqc.LoadDataChanged += _ => Dispatcher.Invoke(RefreshPlot);
        DataAqc.ChartCleared += () => Dispatcher.Invoke(ResetPlot);
        DataAqc.Refresh(Dispatcher);
        DataAqc.StartConsumers(Dispatcher);

        if (!_connectedAtStartup)
        {
            Dispatcher.BeginInvoke(() =>
                MessageBox.Error("连接失败，请检查线路！", "TensileNeW"));
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.SaveSettings();
        MainViewModel.StopConsumers();
        Logger.Info("关闭程序");
    }

    private void InitializePlot()
    {
        if (_plotInitialized)
        {
            return;
        }

        LoadPlot.Plot.Title("载荷位移数据");
        LoadPlot.Plot.XLabel("位移");
        LoadPlot.Plot.YLabel("载荷");
        LoadPlot.Plot.Axes.AutoScale();
        LoadPlot.Refresh();
        _plotInitialized = true;
    }

    private void ResetPlot()
    {
        LoadPlot.Plot.Clear();
        LoadPlot.Plot.Title("载荷位移数据");
        LoadPlot.Plot.XLabel("位移");
        LoadPlot.Plot.YLabel("载荷");
        LoadPlot.Refresh();
    }

    private void RefreshPlot()
    {
        if (!_plotInitialized)
        {
            InitializePlot();
        }

        var items = DataAqc.loadModels.Cast<Loadmodel>().TakeLast(50000).ToList();
        LoadPlot.Plot.Clear();
        if (items.Count > 0)
        {
            double[] xs = items.Select(x => (double)x.RealDistance).ToArray();
            double[] ys = items.Select(x => (double)x.RealForce).ToArray();
            LoadPlot.Plot.Add.Scatter(xs, ys);
            LoadPlot.Plot.Axes.AutoScale();
        }

        LoadPlot.Plot.Title("载荷位移数据");
        LoadPlot.Plot.XLabel("位移");
        LoadPlot.Plot.YLabel("载荷");
        LoadPlot.Refresh();
    }

    private void Home_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Home";
    private void Settings_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Settings";
    private void Variables_Click(object sender, RoutedEventArgs e) => _viewModel.CurrentPage = "Variables";

    private async void StartPress_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("压边");
    private async void ReleasePress_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("压边释放");
    private async void StartTensile_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("拉伸");
    private async void ReleaseTensile_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("拉伸释放");
    private async void Stop_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("停止");
    private async void Reset_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("数据重置");
    private async void Calibration_Click(object sender, RoutedEventArgs e) => await _viewModel.PulseAsync("传感器标零");
    private async void WriteRecipe_Click(object sender, RoutedEventArgs e) => await _viewModel.WriteRecipeAsync();

    private async void ClosePress_Down(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("冲程压边", true);
    private async void ClosePress_Up(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("冲程压边", false);
    private async void Tanliao_Down(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("弹料", true);
    private async void Tanliao_Up(object sender, MouseButtonEventArgs e) => await _viewModel.SetBoolAsync("弹料", false);

    private void SaveData_Click(object sender, RoutedEventArgs e) => _viewModel.SaveDataAs();
    private void AddRecipe_Click(object sender, RoutedEventArgs e) => _viewModel.AddRecipe();
    private void DeleteRecipe_Click(object sender, RoutedEventArgs e) => _viewModel.DeleteRecipe();
    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveSettingsAndApplyLanguage();
        Growl.SuccessGlobal("保存成功");
    }

    private void ShutdownRatioBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        string dec = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

        foreach (char ch in e.Text)
        {
            if (char.IsControl(ch)) continue;
            if (!char.IsDigit(ch) && ch.ToString() != dec)
            {
                e.Handled = true;
                return;
            }
        }

        string before = tb.Text.Substring(0, tb.SelectionStart);
        string after = tb.Text.Substring(tb.SelectionStart + tb.SelectionLength);
        string proposed = before + e.Text + after;

        if (proposed == dec) return;
        if (proposed.EndsWith(dec)) return;

        if (decimal.TryParse(proposed, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out decimal val))
        {
            if (val < 0m || val > 1m)
            {
                e.Handled = true;
            }
        }
    }

    private void ShutdownRatioBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        if (string.IsNullOrWhiteSpace(tb.Text)) return;

        string dec = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (tb.Text == dec)
        {
            tb.Text = "0" + dec;
            return;
        }

        if (decimal.TryParse(tb.Text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out decimal val))
        {
            if (val < 0m) val = 0m;
            if (val > 1m) val = 1m;
            tb.Text = val.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
        else
        {
            tb.Text = "0";
        }
    }

    private static PLCVariable FindVariable(string name)
    {
        return DataAqc.PLCVariables.First(t => t.Name == name);
    }
}
