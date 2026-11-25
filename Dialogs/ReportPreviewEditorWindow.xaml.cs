using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OfficeOpenXml;
using TensileNeW.Services;

namespace TensileNeW;

public partial class ReportPreviewEditorWindow : Window
{
    public ReportPreviewEditorWindow(ExcelPackage workbook)
    {
        Workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        TemplateDocument = TestReportService.ReadTemplate(Workbook);
        InitializeComponent();
        RenderTemplate();
    }

    public ExcelPackage Workbook { get; }
    public ReportTemplateDocument TemplateDocument { get; }

    private void RenderTemplate()
    {
        if (TemplateDocument.Sheets.Count == 0)
        {
            TemplateHost.Content = new TextBlock
            {
                Text = "模板中没有可预览的工作表。",
                Margin = new Thickness(16),
                Foreground = Brushes.Gray
            };
            return;
        }

        var sheet = TemplateDocument.Sheets[0];
        var grid = new Grid { Background = Brushes.White, SnapsToDevicePixels = true };
        foreach (double width in sheet.ColumnWidths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Pixel) });
        }
        foreach (double height in sheet.RowHeights)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height, GridUnitType.Pixel) });
        }

        foreach (var cell in sheet.Cells)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(185, 190, 195)),
                BorderThickness = new Thickness(0.5),
                Background = CreateBrush(cell.BackgroundColor) ?? Brushes.White,
                Child = new TextBlock
                {
                    Text = cell.Text,
                    FontFamily = string.IsNullOrWhiteSpace(cell.FontName)
                        ? new FontFamily("Microsoft YaHei")
                        : new FontFamily(cell.FontName),
                    FontSize = Math.Clamp(cell.FontSize, 6, 24),
                    FontWeight = cell.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = cell.Italic ? FontStyles.Italic : FontStyles.Normal,
                    Foreground = CreateBrush(cell.ForegroundColor) ?? Brushes.Black,
                    TextWrapping = cell.WrapText ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    TextAlignment = GetTextAlignment(cell.HorizontalAlignment),
                    VerticalAlignment = GetVerticalAlignment(cell.VerticalAlignment),
                    Margin = new Thickness(2)
                }
            };
            Grid.SetRow(border, cell.Row);
            Grid.SetColumn(border, cell.Column);
            Grid.SetRowSpan(border, cell.RowSpan);
            Grid.SetColumnSpan(border, cell.ColumnSpan);
            grid.Children.Add(border);
        }

        foreach (var image in sheet.Images)
        {
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(image.PngData))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();
            var imageElement = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false
            };
            Grid.SetRow(imageElement, image.Row);
            Grid.SetColumn(imageElement, image.Column);
            Grid.SetRowSpan(imageElement, image.RowSpan);
            Grid.SetColumnSpan(imageElement, image.ColumnSpan);
            grid.Children.Add(imageElement);
        }

        TemplateHost.Content = grid;
        SheetNameText.Text = sheet.Name;
    }

    private static SolidColorBrush? CreateBrush(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static TextAlignment GetTextAlignment(string value) => value switch
    {
        "Center" or "CenterContinuous" => TextAlignment.Center,
        "Right" => TextAlignment.Right,
        "Justify" or "Distributed" => TextAlignment.Justify,
        _ => TextAlignment.Left
    };

    private static VerticalAlignment GetVerticalAlignment(string value) => value switch
    {
        "Center" => VerticalAlignment.Center,
        "Bottom" => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Top
    };
}