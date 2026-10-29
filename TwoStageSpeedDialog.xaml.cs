using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TensileNeW.Models;

namespace TensileNeW;

public partial class TwoStageSpeedDialog : Window
{
    private readonly TextBox[] _first = new TextBox[6];
    private readonly TextBox[] _second = new TextBox[6];
    public RecipeModel? Result { get; private set; }
    public TwoStageSpeedDialog(RecipeModel source)
    {
        InitializeComponent();
        RecipeNameBox.Text = source.RecipeName + "-二段速";
        RecipeStageParameters currentParameters = RecipeStageParameters.FromRecipe(source);
        BuildInputs(FirstGrid, _first, currentParameters);
        BuildInputs(SecondGrid, _second, currentParameters);
    }
    private static void BuildInputs(Grid grid, TextBox[] boxes, RecipeStageParameters values)
    {
        string[] labels = ["冲程压边力", "闭环压边力", "停机延时", "停机比例", "速度", "拉伸位移上限"];
        for (int i = 0; i < labels.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = labels[i], Width = 110, VerticalAlignment = VerticalAlignment.Center });
            boxes[i] = new TextBox { Height = 30, Width = 120, VerticalContentAlignment = VerticalAlignment.Center };
            boxes[i].Text = i switch { 0 => values.StrokeStampingForce.ToString(CultureInfo.InvariantCulture), 1 => values.ClosedLoopStampingForce.ToString(CultureInfo.InvariantCulture), 2 => values.ShutdownDelay.ToString(CultureInfo.InvariantCulture), 3 => values.ShutdownRatio.ToString(CultureInfo.InvariantCulture), 4 => values.Speed.ToString(CultureInfo.InvariantCulture), _ => values.TensileDistanceLimit.ToString(CultureInfo.InvariantCulture) };
            panel.Children.Add(boxes[i]); Grid.SetRow(panel, i); grid.Children.Add(panel);
        }
    }
    private static bool TryRead(TextBox[] boxes, out RecipeStageParameters values)
    {
        values = new();
        if (!float.TryParse(boxes[0].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float strokeStampingForce) ||
            !float.TryParse(boxes[1].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float closedLoopStampingForce) ||
            !ushort.TryParse(boxes[2].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort shutdownDelay) ||
            !float.TryParse(boxes[3].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float shutdownRatio) ||
            !float.TryParse(boxes[4].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float speed) ||
            !float.TryParse(boxes[5].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float tensileDistanceLimit)) return false;
        values.StrokeStampingForce = strokeStampingForce;
        values.ClosedLoopStampingForce = closedLoopStampingForce;
        values.ShutdownDelay = shutdownDelay;
        values.ShutdownRatio = shutdownRatio;
        values.Speed = speed;
        values.TensileDistanceLimit = tensileDistanceLimit;
        return true;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RecipeNameBox.Text) || !TryRead(_first, out var first) || !TryRead(_second, out var second))
        {
            ErrorTextBlock.Text = "请输入有效的配方名称和参数。";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        ErrorTextBlock.Visibility = Visibility.Collapsed;
        Result = new RecipeModel
        {
            RecipeName = RecipeNameBox.Text.Trim(),
            IsTwoStageSpeed = true,
            FirstStageParameters = first,
            SecondStageParameters = second,
            StrokeStampingForce = first.StrokeStampingForce,
            ClosedLoopStampingForce = first.ClosedLoopStampingForce,
            ShutdownDelay = first.ShutdownDelay,
            ShutdownRatio = first.ShutdownRatio,
            Speed = first.Speed,
            TensileDistanceLimit = first.TensileDistanceLimit
        };
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
}
