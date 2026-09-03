using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using NPOI.OpenXmlFormats.Wordprocessing;
using NPOI.Util;
using NPOI.WP.UserModel;
using NPOI.XWPF.UserModel;
using TensileNeW.Models;

namespace TensileNeW.Services;

public static class TestReportService
{
    private const ulong A4WidthTwips = 11906;
    private const ulong A4HeightTwips = 16838;
    private const ulong PageHorizontalMarginTwips = 900;
    private const ulong PageTopMarginTwips = 720;
    private const ulong PageBottomMarginTwips = 540;
    private const int ParameterTableWidthTwips = 7600;
    private const int ReportBodyIndentTwips = 353;

    public static void Save(
        string fileName,
        string imagePath,
        string recipeName,
        string trialSerialNumber,
        DateTime generatedAt,
        string maxForce,
        string validDistance,
        RecipeModel? recipe,
        string annotationName,
        string annotationContent)
    {
        using var document = new XWPFDocument();
        ConfigurePage(document);
        AddLogoHeader(document);

        AddParagraph(document, "试验报告", 18, bold: true, ParagraphAlignment.CENTER);
        AddParagraph(
            document,
            $"试验名称：{recipeName}    试验序列号：{trialSerialNumber}    生成时间：{generatedAt:yyyy-MM-dd HH:mm:ss}",
            11,
            bold: false,
            ParagraphAlignment.CENTER);

        AddPictureParagraph(document, imagePath, maxWidth: 470, maxHeight: 285, ParagraphAlignment.CENTER);

        AddParagraph(document, "试验结果", 14, bold: true, indentationLeft: ReportBodyIndentTwips);
        AddParagraph(document, $"最大拉伸力：{WithUnit(maxForce, "KN")}", 12, indentationLeft: ReportBodyIndentTwips);
        AddParagraph(document, $"有效拉伸位移：{WithUnit(validDistance, "mm")}", 12, indentationLeft: ReportBodyIndentTwips);

        AddParameterSection(document, recipe);
        AddAnnotationSection(document, annotationName, annotationContent);

        using var stream = File.Create(fileName);
        document.Write(stream);
    }

    private static void ConfigurePage(XWPFDocument document)
    {
        CT_SectPr section = document.Document.body.sectPr ?? new CT_SectPr();
        document.Document.body.sectPr = section;

        section.pgSz = new CT_PageSz
        {
            w = A4WidthTwips,
            h = A4HeightTwips
        };

        section.pgMar = new CT_PageMar
        {
            left = PageHorizontalMarginTwips,
            right = PageHorizontalMarginTwips,
            top = PageTopMarginTwips,
            bottom = PageBottomMarginTwips,
            header = 360,
            footer = 360,
            gutter = 0
        };
    }

    public static string SaveClipboardImageToTempFile()
    {
        BitmapSource? clipboardImage = Clipboard.GetImage();
        if (clipboardImage == null)
        {
            throw new InvalidOperationException("无法从剪贴板获取曲线图图片");
        }

        string tempImagePath = Path.Combine(Path.GetTempPath(), $"TensileReport_{Guid.NewGuid():N}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(clipboardImage));
        using var stream = File.Create(tempImagePath);
        encoder.Save(stream);
        return tempImagePath;
    }

    private static void AddLogoHeader(XWPFDocument document)
    {
        using Stream? logoStream = OpenResourceStream("Assets/GB-LOGO.png");
        if (logoStream is null)
        {
            return;
        }

        using var memoryStream = new MemoryStream();
        logoStream.CopyTo(memoryStream);
        byte[] logoBytes = memoryStream.ToArray();

        AddLogoToHeader(document, HeaderFooterType.DEFAULT, logoBytes);
        AddLogoToHeader(document, HeaderFooterType.FIRST, logoBytes);
        AddLogoToHeader(document, HeaderFooterType.EVEN, logoBytes);
    }

    private static void AddLogoToHeader(XWPFDocument document, HeaderFooterType type, byte[] logoBytes)
    {
        var header = document.CreateHeader(type);
        var paragraph = header.CreateParagraph();
        paragraph.Alignment = ParagraphAlignment.LEFT;

        var run = paragraph.CreateRun();
        using var imageStream = new MemoryStream(logoBytes);
        var size = GetScaledSize(imageStream, maxWidth: 120, maxHeight: 45);
        imageStream.Position = 0;
        run.AddPicture(
            imageStream,
            (int)PictureType.PNG,
            "GB-LOGO.png",
            Units.ToEMU(size.Width),
            Units.ToEMU(size.Height));
    }

    private static Stream? OpenResourceStream(string resourcePath)
    {
        var resourceUri = new Uri($"pack://application:,,,/{resourcePath}", UriKind.Absolute);
        StreamResourceInfo? resourceInfo = Application.GetResourceStream(resourceUri);
        return resourceInfo?.Stream;
    }

    private static void AddParameterSection(XWPFDocument document, RecipeModel? recipe)
    {
        AddParagraph(document, "参数设置", 14, bold: true, indentationLeft: ReportBodyIndentTwips);

        List<(string Name, string Value)> parameters =
        [
            ("配方名称", recipe?.RecipeName ?? string.Empty),
            ("冲程压边力设定", WithUnit(FormatValue(recipe?.StrokeStampingForce), "KN")),
            ("闭环压边力设定", WithUnit(FormatValue(recipe?.ClosedLoopStampingForce), "KN")),
            ("停机延时设定", WithUnit(recipe?.ShutdownDelay.ToString() ?? string.Empty, "S")),
            ("停机比例设定", FormatValue(recipe?.ShutdownRatio)),
            ("拉伸位移上限", WithUnit(FormatValue(recipe?.TensileDistanceLimit), "mm")),
            ("速度设定", WithUnit(FormatValue(recipe?.Speed), "mm/s"))
        ];

        var table = document.CreateTable(parameters.Count + 1, 2);
        ConfigureParameterTable(table);
        SetCellText(table.GetRow(0).GetCell(0), "参数", bold: true);
        SetCellText(table.GetRow(0).GetCell(1), "值", bold: true);

        for (int i = 0; i < parameters.Count; i++)
        {
            SetCellText(table.GetRow(i + 1).GetCell(0), parameters[i].Name);
            SetCellText(table.GetRow(i + 1).GetCell(1), parameters[i].Value);
        }
    }

    private static void AddAnnotationSection(XWPFDocument document, string annotationName, string annotationContent)
    {
        if (string.IsNullOrWhiteSpace(annotationName) && string.IsNullOrWhiteSpace(annotationContent))
        {
            return;
        }

        AddParagraph(document, "批注", 14, bold: true, indentationLeft: ReportBodyIndentTwips);
        if (!string.IsNullOrWhiteSpace(annotationName))
        {
            AddParagraph(document, annotationName, 12, indentationLeft: ReportBodyIndentTwips);
        }

        if (!string.IsNullOrWhiteSpace(annotationContent))
        {
            AddParagraph(document, annotationContent, 12, indentationLeft: ReportBodyIndentTwips);
        }
    }

    private static void AddParagraph(
        XWPFDocument document,
        string text,
        int fontSize,
        bool bold = false,
        ParagraphAlignment alignment = ParagraphAlignment.LEFT,
        int? indentationLeft = null)
    {
        var paragraph = document.CreateParagraph();
        paragraph.Alignment = alignment;
        paragraph.SpacingBefore = 0;
        paragraph.SpacingAfter = 60;
        if (indentationLeft.HasValue)
        {
            paragraph.IndentationLeft = indentationLeft.Value;
        }

        var run = paragraph.CreateRun();
        run.FontSize = fontSize;
        run.IsBold = bold;
        run.SetText(text);
    }

    private static void AddPictureParagraph(
        XWPFDocument document,
        string imagePath,
        int maxWidth,
        int maxHeight,
        ParagraphAlignment alignment)
    {
        var paragraph = document.CreateParagraph();
        paragraph.Alignment = alignment;
        paragraph.SpacingBefore = 0;
        paragraph.SpacingAfter = 60;
        var run = paragraph.CreateRun();

        using var stream = File.OpenRead(imagePath);
        var size = GetScaledSize(stream, maxWidth, maxHeight);
        stream.Position = 0;
        run.AddPicture(
            stream,
            (int)PictureType.PNG,
            Path.GetFileName(imagePath),
            Units.ToEMU(size.Width),
            Units.ToEMU(size.Height));
    }

    private static void SetCellText(XWPFTableCell cell, string text, bool bold = false)
    {
        var paragraph = cell.Paragraphs.Count > 0 ? cell.Paragraphs[0] : cell.AddParagraph();
        paragraph.SpacingBefore = 0;
        paragraph.SpacingAfter = 0;
        var run = paragraph.CreateRun();
        run.IsBold = bold;
        run.SetText(text);
    }

    private static void ConfigureParameterTable(XWPFTable table)
    {
        table.Width = ParameterTableWidthTwips;

        CT_TblPr tableProperties = table.GetCTTbl().tblPr ?? new CT_TblPr();
        table.GetCTTbl().tblPr = tableProperties;
        tableProperties.tblW = new CT_TblWidth
        {
            type = ST_TblWidth.dxa,
            typeSpecified = true,
            w = ParameterTableWidthTwips.ToString()
        };
        tableProperties.jc = new CT_Jc
        {
            val = ST_Jc.center
        };
    }

    private static string FormatValue(float? value) => value?.ToString("0.###") ?? string.Empty;

    private static string WithUnit(string value, string unit)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{value} {unit}";
    }

    private static (int Width, int Height) GetScaledSize(Stream stream, int maxWidth, int maxHeight)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        int sourceWidth = decoder.Frames[0].PixelWidth;
        int sourceHeight = decoder.Frames[0].PixelHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return (maxWidth, maxHeight);
        }

        double scale = Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight);
        scale = Math.Min(scale, 1.0);
        return (
            Math.Max(1, (int)Math.Round(sourceWidth * scale)),
            Math.Max(1, (int)Math.Round(sourceHeight * scale)));
    }
}
