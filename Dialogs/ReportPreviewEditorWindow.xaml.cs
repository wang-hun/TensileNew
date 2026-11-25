using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfiumViewer;
using TensileNeW.Services;

namespace TensileNeW;

public partial class ReportPreviewEditorWindow : Window
{
    private const float RenderDpi = 144f;
    private readonly ReportEditorDocument _document;
    private readonly Dictionary<string, FrameworkElement> _fieldControls = new(StringComparer.Ordinal);

    public ReportPreviewEditorWindow(ReportEditorDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        InitializeComponent();
        RenderDocument();
    }

    public ReportEditorResult? Result { get; private set; }

    private void RenderDocument()
    {
        if (!File.Exists(_document.PdfPath))
        {
            throw new FileNotFoundException("找不到报告 PDF 模板。", _document.PdfPath);
        }

        using var pdf = PdfDocument.Load(_document.PdfPath);
        ValidateFieldDefinitions(pdf.PageCount);
        PageCountText.Text = $"共 {pdf.PageCount} 页";

        for (int pageIndex = 0; pageIndex < pdf.PageCount; pageIndex++)
        {
            using var renderedPage = pdf.Render(pageIndex, RenderDpi, RenderDpi, PdfRenderFlags.Annotations);
            using var stream = new MemoryStream();
            renderedPage.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            double pageWidth = bitmap.PixelWidth * 96d / RenderDpi;
            double pageHeight = bitmap.PixelHeight * 96d / RenderDpi;
            var pageSurface = new Grid
            {
                Width = pageWidth,
                Height = pageHeight,
                Background = Brushes.White,
                SnapsToDevicePixels = true
            };
            pageSurface.Children.Add(new Image
            {
                Source = bitmap,
                Width = pageWidth,
                Height = pageHeight,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            });

            var overlay = new Canvas { Width = pageWidth, Height = pageHeight, ClipToBounds = true };
            foreach (ReportEditorField field in _document.Fields.Where(field => field.PageIndex == pageIndex))
            {
                FrameworkElement control = CreateFieldControl(field);
                control.Width = field.Width * pageWidth;
                control.Height = field.Height * pageHeight;
                Canvas.SetLeft(control, field.Left * pageWidth);
                Canvas.SetTop(control, field.Top * pageHeight);
                overlay.Children.Add(control);
            }
            pageSurface.Children.Add(overlay);
            PageStackPanel.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(190, 195, 200)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 16),
                Child = pageSurface
            });
        }
    }

    private void ValidateFieldDefinitions(int pageCount)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ReportEditorField field in _document.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Id) || !ids.Add(field.Id))
            {
                throw new InvalidDataException("报告输入项必须具有唯一且非空的标识。");
            }
            if (string.IsNullOrWhiteSpace(field.Label) || field.PageIndex < 0 || field.PageIndex >= pageCount ||
                field.Left < 0 || field.Top < 0 || field.Width <= 0 || field.Height <= 0 ||
                field.Left + field.Width > 1 || field.Top + field.Height > 1)
            {
                throw new InvalidDataException($"报告输入项「{field.Id}」的页面位置无效。");
            }
            if (field.Kind == ReportEditorFieldKind.Dropdown && field.Options.Count == 0)
            {
                throw new InvalidDataException($"下拉项「{field.Label}」没有候选值。");
            }
        }
    }

    private FrameworkElement CreateFieldControl(ReportEditorField field)
    {
        FrameworkElement input = field.Kind switch
        {
            ReportEditorFieldKind.Text => new TextBox { Text = field.InitialValue ?? string.Empty },
            ReportEditorFieldKind.DateTime => CreateDateTimeInput(field),
            ReportEditorFieldKind.Dropdown => CreateDropdownInput(field),
            _ => throw new InvalidDataException($"不支持的报告输入类型：{field.Kind}")
        };
        input.ToolTip = field.Label;
        input.Margin = new Thickness(0);
        if (input is Control control)
        {
            control.Background = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255));
        }
        _fieldControls.Add(field.Id, input);
        return input;
    }

    private static FrameworkElement CreateDateTimeInput(ReportEditorField field)
    {
        DateTime? initial = DateTime.TryParse(field.InitialValue, out DateTime parsed) ? parsed : null;
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        var datePicker = new DatePicker { SelectedDate = initial?.Date, VerticalContentAlignment = VerticalAlignment.Center };
        var timeInput = new TextBox
        {
            Text = initial?.ToString("HH:mm") ?? string.Empty,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "时间（时:分）"
        };
        Grid.SetColumn(timeInput, 1);
        panel.Children.Add(datePicker);
        panel.Children.Add(timeInput);
        panel.Tag = (datePicker, timeInput);
        return panel;
    }

    private static FrameworkElement CreateDropdownInput(ReportEditorField field)
    {
        var comboBox = new ComboBox
        {
            ItemsSource = field.Options,
            SelectedItem = field.InitialValue,
            IsEditable = false,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (comboBox.SelectedIndex < 0 && field.Options.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
        return comboBox;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (ReportEditorField field in _document.Fields)
        {
            string? value = GetFieldValue(_fieldControls[field.Id]);
            if (field.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                ValidationText.Text = $"请填写「{field.Label}」。";
                return;
            }
            values[field.Id] = value;
        }
        Result = new ReportEditorResult { Values = values };
        DialogResult = true;
    }

    private static string? GetFieldValue(FrameworkElement control)
    {
        if (control is TextBox textBox)
        {
            return textBox.Text.Trim();
        }
        if (control is ComboBox comboBox)
        {
            return comboBox.SelectedItem?.ToString();
        }
        if (control is Grid { Tag: ValueTuple<DatePicker, TextBox> dateTimeInput })
        {
            if (!dateTimeInput.Item1.SelectedDate.HasValue ||
                !TimeSpan.TryParse(dateTimeInput.Item2.Text, out TimeSpan time) ||
                time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            {
                return string.Empty;
            }
            return dateTimeInput.Item1.SelectedDate.Value.Date.Add(time).ToString("yyyy-MM-dd HH:mm");
        }
        return null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }
}