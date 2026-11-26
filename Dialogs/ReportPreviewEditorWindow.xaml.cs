using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfiumViewer;
using TensileNeW.Services;

namespace TensileNeW;

public partial class ReportPreviewEditorWindow : Window
{
    private sealed record PreviewPage(Grid Host, Viewbox Scaler, Image Image, int PageIndex, double Width, double Height);

    private const double LogicalDpi = 96d;
    private const double PdfPointsPerInch = 72d;
    private const float MinimumRenderDpi = 300f;
    private const double MinimumZoom = 0.25;
    private const double MaximumZoom = 4.0;
    private readonly ReportEditorDocument _document;
    private readonly ReportEditorValueStore _valueStore = new();
    private readonly Dictionary<string, string> _savedValues;
    private readonly Dictionary<string, FrameworkElement> _fieldControls = new(StringComparer.Ordinal);
    private readonly List<PreviewPage> _pages = [];
    private PdfDocument? _pdf;
    private bool _fitToWidth = true;
    private double _manualZoom = 1.0;

    public ReportPreviewEditorWindow(ReportEditorDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _savedValues = _valueStore.Load();
        InitializeComponent();
        PageScrollViewer.SizeChanged += PageScrollViewer_SizeChanged;
        PageScrollViewer.PreviewMouseWheel += PageScrollViewer_PreviewMouseWheel;
        Loaded += (_, _) => ApplyZoom();
        Closed += (_, _) => DisposePdf();
        RenderDocument();
    }

    public ReportEditorResult? Result { get; private set; }

    private void RenderDocument()
    {
        if (!File.Exists(_document.PdfPath))
        {
            throw new FileNotFoundException("找不到报告 PDF 模板。", _document.PdfPath);
        }

        _pdf = PdfDocument.Load(_document.PdfPath);
        PdfDocument pdf = _pdf;
        ValidateFieldDefinitions(pdf.PageCount);
        PageCountText.Text = $"共 {pdf.PageCount} 页";

        for (int pageIndex = 0; pageIndex < pdf.PageCount; pageIndex++)
        {
            var pageSize = pdf.PageSizes[pageIndex];
            double pageWidth = pageSize.Width * LogicalDpi / PdfPointsPerInch;
            double pageHeight = pageSize.Height * LogicalDpi / PdfPointsPerInch;
            BitmapSource bitmap = RenderPage(pageIndex, MinimumRenderDpi);
            var pageSurface = new Grid
            {
                Width = pageWidth,
                Height = pageHeight,
                Background = Brushes.White,
                SnapsToDevicePixels = true
            };
            var pageImage = new Image
            {
                Source = bitmap,
                Width = pageWidth,
                Height = pageHeight,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(pageImage, BitmapScalingMode.HighQuality);
            pageSurface.Children.Add(pageImage);

            var overlay = new Canvas { Width = pageWidth, Height = pageHeight, ClipToBounds = true };
            foreach (ReportEditorField field in _document.Fields.Where(field => field.PageIndex == pageIndex))
            {
                string? initialValue = field.Kind is ReportEditorFieldKind.Date or ReportEditorFieldKind.DateTime
                    ? null
                    : _savedValues.TryGetValue(field.Id, out string? savedValue) ? savedValue : field.InitialValue;
                FrameworkElement control = CreateFieldControl(field, initialValue);
                control.Width = field.Width * pageWidth;
                control.Height = field.Height * pageHeight;
                Canvas.SetLeft(control, field.Left * pageWidth);
                Canvas.SetTop(control, field.Top * pageHeight);
                overlay.Children.Add(control);
            }
            pageSurface.Children.Add(overlay);
            var pageBorder = new Border
            {
                Width = pageWidth + 2,
                Height = pageHeight + 2,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(190, 195, 200)),
                BorderThickness = new Thickness(1),
                Child = pageSurface
            };
            var pageScaler = new Viewbox
            {
                Width = pageWidth + 2,
                Height = pageHeight + 2,
                Stretch = Stretch.Fill,
                Child = pageBorder
            };
            var pageHost = new Grid
            {
                Width = pageWidth + 2,
                Height = pageHeight + 2,
                Margin = new Thickness(0, 0, 0, 16),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            pageHost.Children.Add(pageScaler);
            _pages.Add(new PreviewPage(pageHost, pageScaler, pageImage, pageIndex, pageWidth + 2, pageHeight + 2));
            PageStackPanel.Children.Add(pageHost);
        }
        ApplyZoom();
    }

    private void PageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitToWidth)
        {
            ApplyZoom();
        }
    }

    private void PageScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0)
        {
            return;
        }

        AdjustZoom(e.Delta > 0 ? 1.2 : 1 / 1.2);
        e.Handled = true;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        AdjustZoom(1.2);
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        AdjustZoom(1 / 1.2);
    }

    private void FitWidth_Click(object sender, RoutedEventArgs e)
    {
        _fitToWidth = true;
        ApplyZoom();
    }

    private void AdjustZoom(double factor)
    {
        if (_fitToWidth)
        {
            _manualZoom = CalculateFitWidthZoom();
            _fitToWidth = false;
        }

        _manualZoom = Math.Clamp(_manualZoom * factor, MinimumZoom, MaximumZoom);
        ApplyZoom();
    }

    private double CalculateFitWidthZoom()
    {
        if (_pages.Count == 0 || PageScrollViewer.ViewportWidth <= 0)
        {
            return 1.0;
        }

        double widestPage = _pages.Max(page => page.Width);
        double availableWidth = Math.Max(1, PageScrollViewer.ViewportWidth - 24);
        return Math.Clamp(availableWidth / widestPage, MinimumZoom, MaximumZoom);
    }

    private void ApplyZoom()
    {
        if (_pages.Count == 0)
        {
            return;
        }

        double zoom = _fitToWidth ? CalculateFitWidthZoom() : _manualZoom;
        EnsureRenderQuality(zoom);
        foreach (PreviewPage page in _pages)
        {
            page.Host.Width = page.Width * zoom;
            page.Host.Height = page.Height * zoom;
            page.Scaler.Width = page.Width * zoom;
            page.Scaler.Height = page.Height * zoom;
        }

        ZoomLevelText.Text = $"{zoom:P0}";
    }

    private BitmapSource RenderPage(int pageIndex, float renderDpi)
    {
        if (_pdf is null)
        {
            throw new InvalidOperationException(nameof(_pdf));
        }

        var pageSize = _pdf.PageSizes[pageIndex];
        int renderWidth = Math.Max(1, (int)Math.Ceiling(pageSize.Width * renderDpi / PdfPointsPerInch));
        int renderHeight = Math.Max(1, (int)Math.Ceiling(pageSize.Height * renderDpi / PdfPointsPerInch));
        using var renderedPage = _pdf.Render(
            pageIndex,
            renderWidth,
            renderHeight,
            renderDpi,
            renderDpi,
            PdfRenderFlags.Annotations | PdfRenderFlags.LcdText | PdfRenderFlags.ForPrinting);
        using var stream = new MemoryStream();
        renderedPage.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void EnsureRenderQuality(double zoom)
    {
        double deviceScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1d;
        float requiredDpi = (float)Math.Max(MinimumRenderDpi, LogicalDpi * zoom * deviceScale);

        foreach (PreviewPage page in _pages)
        {
            if (page.Image.Source is BitmapSource bitmap &&
                bitmap.PixelWidth >= Math.Ceiling((page.Width - 2) * zoom * deviceScale))
            {
                continue;
            }

            page.Image.Source = RenderPage(page.PageIndex, requiredDpi);
        }
    }

    private void DisposePdf()
    {
        _pdf?.Dispose();
        _pdf = null;
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

        }
    }

    private FrameworkElement CreateFieldControl(ReportEditorField field, string? initialValue)
    {
        FrameworkElement input = field.Kind switch
        {
            ReportEditorFieldKind.Text => new TextBox { Text = initialValue ?? string.Empty },
            ReportEditorFieldKind.Date => CreateDateInput(),
            ReportEditorFieldKind.DateTime => CreateDateTimeInput(),
            ReportEditorFieldKind.Dropdown => CreateDropdownInput(field, initialValue),
            _ => throw new InvalidDataException($"不支持的报告输入类型：{field.Kind}")
        };
        input.ToolTip = field.Label;
        input.Margin = new Thickness(0);
        if (input is Control control)
        {
            control.Background = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255));
        }
        _fieldControls.Add(field.Id, input);
        if (field.Kind is not (ReportEditorFieldKind.Date or ReportEditorFieldKind.DateTime))
        {
            if (input is TextBox textBox)
            {
                textBox.TextChanged += (_, _) => PersistFieldValue(field.Id, textBox.Text);
            }
            else if (input is ComboBox comboBox)
            {
                comboBox.SelectionChanged += (_, _) => PersistFieldValue(field.Id,
                    comboBox.IsEditable ? comboBox.Text : comboBox.SelectedItem?.ToString());
                if (comboBox.IsEditable)
                {
                    comboBox.AddHandler(TextBox.TextChangedEvent,
                        new TextChangedEventHandler((_, _) => PersistFieldValue(field.Id, comboBox.Text)));
                }
            }
        }
        return input;
    }

    private static DatePicker CreateDateInput()
    {
        var datePicker = new DatePicker
        {
            Language = XmlLanguage.GetLanguage("en-GB"),
            SelectedDateFormat = DatePickerFormat.Short,
            SelectedDate = DateTime.Today,
            FontSize = 11,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        datePicker.SelectedDateChanged += (_, _) =>
        {
            if (datePicker.SelectedDate is DateTime selected)
            {
                datePicker.Text = selected.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            }
        };
        datePicker.DateValidationError += (_, args) => args.ThrowException = false;
        datePicker.Text = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return datePicker;
    }
    private static FrameworkElement CreateDateTimeInput()
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        var datePicker = new DatePicker
        {
            Language = XmlLanguage.GetLanguage("en-GB"),
            SelectedDateFormat = DatePickerFormat.Short,
            SelectedDate = DateTime.Today,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var timeInput = new TextBox
        {
            Text = string.Empty,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "时间（时:分）"
        };
        Grid.SetColumn(timeInput, 1);
        panel.Children.Add(datePicker);
        panel.Children.Add(timeInput);
        panel.Tag = (datePicker, timeInput);
        return panel;
    }

    private static FrameworkElement CreateDropdownInput(ReportEditorField field, string? initialValue)
    {
        var comboBox = new ComboBox
        {
            ItemsSource = field.Options,
            SelectedItem = initialValue,
            IsEditable = field.Options.Count == 0,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (comboBox.SelectedIndex < 0 && field.Options.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
        return comboBox;
    }

    private void PersistFieldValue(string fieldId, string? value)
    {
        _savedValues[fieldId] = value ?? string.Empty;
        try
        {
            _valueStore.Save(_savedValues);
        }
        catch (IOException)
        {
            ValidationText.Text = "无法保存填写记忆，请检查用户数据目录权限。";
        }
        catch (UnauthorizedAccessException)
        {
            ValidationText.Text = "无法保存填写记忆，请检查用户数据目录权限。";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (ReportEditorField field in _document.Fields)
        {
            if (_fieldControls[field.Id] is DatePicker datePicker &&
                !string.IsNullOrWhiteSpace(datePicker.Text) &&
                !DateTime.TryParseExact(datePicker.Text.Trim(), "dd/MM/yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                ValidationText.Text = $"「{field.Label}」请按日/月/年填写日期（dd/MM/yyyy）。";
                datePicker.Focus();
                return;
            }
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
        if (control is DatePicker datePicker)
        {
            return DateTime.TryParseExact(datePicker.Text.Trim(), "dd/MM/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
                ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : string.Empty;
        }
        if (control is ComboBox comboBox)
        {
            return comboBox.IsEditable ? comboBox.Text.Trim() : comboBox.SelectedItem?.ToString();
        }
        if (control is Grid { Tag: ValueTuple<DatePicker, TextBox> dateTimeInput })
        {
            if (!dateTimeInput.Item1.SelectedDate.HasValue ||
                !TimeSpan.TryParse(dateTimeInput.Item2.Text, out TimeSpan time) ||
                time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            {
                return string.Empty;
            }
            return dateTimeInput.Item1.SelectedDate.Value.Date.Add(time).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        }
        return null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }
}
