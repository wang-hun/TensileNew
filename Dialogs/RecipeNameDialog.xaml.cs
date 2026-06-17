using HandyControl.Controls;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TensileNeW.Models;

namespace TensileNeW;

public partial class RecipeNameDialog : UserControl
{
    private readonly ObservableCollection<TemplateOption> _templateOptions = [];

    public event EventHandler<RecipeNameConfirmedEventArgs>? Confirmed;

    public RecipeNameDialog()
    {
        InitializeComponent();
        LoadTemplateOptions();
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var selected = TemplateComboBox.SelectedItem as TemplateOption;
        Confirmed?.Invoke(this, new RecipeNameConfirmedEventArgs(InputBox.Text.Trim(), selected?.Recipe));
        CloseDialog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Ok_Click(sender, e);
        }
    }

    private void LoadTemplateOptions()
    {
        _templateOptions.Clear();
        _templateOptions.Add(new TemplateOption("不复制默认模版", null));

        foreach (var recipe in RAM.BuiltInRecipes)
        {
            _templateOptions.Add(new TemplateOption(recipe.RecipeName, recipe));
        }

        TemplateComboBox.ItemsSource = _templateOptions;
        TemplateComboBox.SelectedIndex = 0;
    }

    private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = TemplateComboBox.SelectedItem as TemplateOption;
        var recipe = selected?.Recipe;
        TemplatePreviewBorder.Visibility = recipe == null ? Visibility.Collapsed : Visibility.Visible;
        if (recipe == null)
        {
            return;
        }

        TemplateNamePreviewText.Text = $"模版：{recipe.RecipeName}";
        StrokeForcePreviewText.Text = $"冲程压边力设定：{recipe.StrokeStampingForce:0.###} KN";
        ClosedLoopPreviewText.Text = $"闭环压边力设定：{recipe.ClosedLoopStampingForce:0.###} KN";
        ShutdownDelayPreviewText.Text = $"停机延时设定：{recipe.ShutdownDelay} (10ms)";
        ShutdownRatioPreviewText.Text = $"停机比例设定：{recipe.ShutdownRatio:0.###}";
        SpeedPreviewText.Text = $"速度设定：{recipe.Speed:0.###} mm/s";
        DistanceLimitPreviewText.Text = $"拉伸位移上限：{recipe.TensileDistanceLimit:0.###} mm";
    }

    private void CloseDialog()
    {
        DependencyObject? current = this;
        while (current != null)
        {
            if (current is Dialog dialog)
            {
                dialog.Close();
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }

    private sealed record TemplateOption(string DisplayName, RecipeModel? Recipe);
}

public sealed class RecipeNameConfirmedEventArgs(string name, RecipeModel? templateRecipe) : EventArgs
{
    public string Name { get; } = name;
    public RecipeModel? TemplateRecipe { get; } = templateRecipe;
}
