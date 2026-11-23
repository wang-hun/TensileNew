using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TensileNeW.Controls;
using TensileNeW.Services;
using IOPath = System.IO.Path;

namespace TensileNeW;

public sealed record VisionDefectAnnotation(string Id, string Type, string TypeName, IReadOnlyList<double> BoundingBox);

public partial class VisionDefectWindow : Window
{
    private readonly ZoomableImageViewport _imageViewport;

    public VisionDefectWindow()
    {
        InitializeComponent();
        _imageViewport = new ZoomableImageViewport(ImageViewport, ImageCanvas);
    }

    public void ShowResult(string imagePath, IReadOnlyList<VisionDefectAnnotation>? defects = null, RingPhysicalDimensions? physicalDimensions = null, string? physicalDimensionsError = null)
    {
        SetPhysicalDimensions(physicalDimensions, physicalDimensionsError);
        ImageCanvas.Children.Clear();
        if (!File.Exists(imagePath))
        {
            EmptyTextBlock.Visibility = Visibility.Visible;
            TitleTextBlock.Text = $"视觉缺陷结果（图片不存在：{IOPath.GetFileName(imagePath)}）";
            return;
        }

        BitmapImage image = new();
        try
        {
            image.BeginInit();
            image.UriSource = new Uri(IOPath.GetFullPath(imagePath));
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.EndInit();
            image.Freeze();
        }
        catch (Exception exception)
        {
            EmptyTextBlock.Visibility = Visibility.Visible;
            TitleTextBlock.Text = $"视觉缺陷结果（图片读取失败：{exception.Message}）";
            return;
        }

        ImageCanvas.Width = image.PixelWidth;
        ImageCanvas.Height = image.PixelHeight;
        ImageCanvas.Children.Add(new Image
        {
            Source = image,
            Width = image.PixelWidth,
            Height = image.PixelHeight,
            Stretch = System.Windows.Media.Stretch.Fill,
            IsHitTestVisible = false
        });

        foreach (VisionDefectAnnotation defect in defects ?? []) DrawDefect(defect);

        EmptyTextBlock.Visibility = Visibility.Collapsed;
        TitleTextBlock.Text = defects is { Count: > 0 }
            ? $"视觉缺陷结果：{defects.Count} 个缺陷（{IOPath.GetFileName(imagePath)}）"
            : $"视觉缺陷结果（{IOPath.GetFileName(imagePath)}）";
        Dispatcher.BeginInvoke(_imageViewport.Reset);
    }

    public void SetPhysicalDimensions(RingPhysicalDimensions? dimensions, string? error = null)
    {
        if (dimensions is null)
        {
            PhysicalDiameterTextBlock.Text = "物理直径：-- mm";
            PhysicalHeightTextBlock.Text = "物理高度：-- mm";
            PhysicalDimensionStatusTextBlock.Text = string.IsNullOrWhiteSpace(error) ? "物理尺寸不可用" : error;
            return;
        }

        PhysicalDiameterTextBlock.Text = $"物理直径：{dimensions.DiameterMillimeters.ToString("F2", CultureInfo.InvariantCulture)} mm";
        PhysicalHeightTextBlock.Text = $"物理高度：{dimensions.HeightMillimeters.ToString("F2", CultureInfo.InvariantCulture)} mm";
        PhysicalDimensionStatusTextBlock.Text = string.Empty;
    }

    private void DrawDefect(VisionDefectAnnotation defect)
    {
        IReadOnlyList<double> box = defect.BoundingBox;
        if (box.Count < 4 || box.Take(4).Any(value => !double.IsFinite(value))) return;
        double left = Math.Min(box[0], box[2]);
        double top = Math.Min(box[1], box[3]);
        double width = Math.Abs(box[2] - box[0]);
        double height = Math.Abs(box[3] - box[1]);
        Brush stroke = defect.Type.Equals("broken", StringComparison.OrdinalIgnoreCase)
            ? Brushes.Orange
            : Brushes.Red;

        Rectangle rectangle = new()
        {
            Width = Math.Max(2, width),
            Height = Math.Max(2, height),
            Stroke = stroke,
            StrokeThickness = 3,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, top);
        ImageCanvas.Children.Add(rectangle);

        Color labelColor = defect.Type.Equals("broken", StringComparison.OrdinalIgnoreCase) ? Colors.Orange : Colors.Red;
        Border label = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(210, labelColor.R, labelColor.G, labelColor.B)),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock { Text = $"{defect.Id} {defect.TypeName}", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, Math.Max(0, top - 25));
        ImageCanvas.Children.Add(label);
    }

    private void ResetView_Click(object sender, RoutedEventArgs e) => _imageViewport.Reset();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}