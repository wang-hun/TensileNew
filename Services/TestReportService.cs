using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using TensileNeW.Models;
using Xceed.Document.NET;
using Xceed.Words.NET;

namespace TensileNeW.Services;

public static class TestReportService
{
    public static void Save(
        string fileName,
        string imagePath,
        string recipeName,
        string trialSerialNumber,
        DateTime generatedAt,
        string maxForce,
        string validDistance,
        RecipeModel? recipe)
    {
        using var document = DocX.Create(fileName);
        document.Sections[0].PageLayout.Orientation = Orientation.Portrait;
        AddLogoHeader(document);

        var title = document.InsertParagraph("试验报告");
        title.Alignment = Alignment.center;
        title.FontSize(18).Bold();

        var info = document.InsertParagraph(
            $"试验名称：{recipeName}    试验序列号：{trialSerialNumber}    生成时间：{generatedAt:yyyy-MM-dd HH:mm:ss}");
        info.Alignment = Alignment.center;
        info.FontSize(11);

        var picture = document.AddImage(imagePath).CreatePicture();
        FitPicture(picture, imagePath, maxWidth: 500, maxHeight: 330);
        var pictureParagraph = document.InsertParagraph();
        pictureParagraph.Alignment = Alignment.center;
        pictureParagraph.AppendPicture(picture);

        var resultTitle = document.InsertParagraph("试验结果");
        resultTitle.FontSize(14).Bold();

        var maxForceParagraph = document.InsertParagraph($"最大拉伸力：{WithUnit(maxForce, "KN")}");
        maxForceParagraph.FontSize(12);
        var validDistanceParagraph = document.InsertParagraph($"有效拉伸位移：{WithUnit(validDistance, "mm")}");
        validDistanceParagraph.FontSize(12);

        AddParameterSection(document, recipe);
        document.Save();
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

    private static void AddLogoHeader(DocX document)
    {
        using Stream? logoStream = OpenResourceStream("Assets/GB-LOGO.png");
        if (logoStream is null)
        {
            return;
        }

        using var memoryStream = new MemoryStream();
        logoStream.CopyTo(memoryStream);
        byte[] logoBytes = memoryStream.ToArray();

        document.AddHeaders();
        AddLogoToHeader(document, document.Headers.Odd, logoBytes);
        AddLogoToHeader(document, document.Headers.First, logoBytes);
        AddLogoToHeader(document, document.Headers.Even, logoBytes);
    }

    private static void AddLogoToHeader(DocX document, Header header, byte[] logoBytes)
    {
        using var imageStream = new MemoryStream(logoBytes);
        var picture = document.AddImage(imageStream).CreatePicture();
        FitPicture(picture, logoBytes, maxWidth: 120, maxHeight: 45);
        var paragraph = header.InsertParagraph();
        paragraph.Alignment = Alignment.left;
        paragraph.AppendPicture(picture);
    }

    private static Stream? OpenResourceStream(string resourcePath)
    {
        var resourceUri = new Uri($"pack://application:,,,/{resourcePath}", UriKind.Absolute);
        StreamResourceInfo? resourceInfo = Application.GetResourceStream(resourceUri);
        return resourceInfo?.Stream;
    }

    private static void AddParameterSection(DocX document, RecipeModel? recipe)
    {
        var title = document.InsertParagraph("参数设置");
        title.FontSize(14).Bold();

        List<(string Name, string Value)> parameters =
        [
            ("配方名称", recipe?.RecipeName ?? string.Empty),
            ("冲程压边力设定", WithUnit(FormatValue(recipe?.StrokeStampingForce), "KN")),
            ("闭环压边力设定", WithUnit(FormatValue(recipe?.ClosedLoopStampingForce), "KN")),
            ("停机延时设定", WithUnit(recipe?.ShutdownDelay.ToString() ?? string.Empty, "S")),
            ("停机比例设定", FormatValue(recipe?.ShutdownRatio)),
            ("速度设定", WithUnit(FormatValue(recipe?.Speed), "mm/s"))
        ];

        var table = document.AddTable(parameters.Count + 1, 2);
        table.Design = TableDesign.TableGrid;
        table.Rows[0].Cells[0].Paragraphs[0].Append("参数").Bold();
        table.Rows[0].Cells[1].Paragraphs[0].Append("值").Bold();

        for (int i = 0; i < parameters.Count; i++)
        {
            table.Rows[i + 1].Cells[0].Paragraphs[0].Append(parameters[i].Name);
            table.Rows[i + 1].Cells[1].Paragraphs[0].Append(parameters[i].Value);
        }

        document.InsertTable(table);
    }

    private static string FormatValue(float? value) => value?.ToString("0.###") ?? string.Empty;

    private static string WithUnit(string value, string unit)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{value} {unit}";
    }

    private static void FitPicture(Picture picture, string imagePath, int maxWidth, int maxHeight)
    {
        using var stream = File.OpenRead(imagePath);
        FitPicture(picture, stream, maxWidth, maxHeight);
    }

    private static void FitPicture(Picture picture, byte[] imageBytes, int maxWidth, int maxHeight)
    {
        using var stream = new MemoryStream(imageBytes);
        FitPicture(picture, stream, maxWidth, maxHeight);
    }

    private static void FitPicture(Picture picture, Stream stream, int maxWidth, int maxHeight)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        int sourceWidth = decoder.Frames[0].PixelWidth;
        int sourceHeight = decoder.Frames[0].PixelHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return;
        }

        double scale = Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight);
        scale = Math.Min(scale, 1.0);
        picture.Width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        picture.Height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
    }
}
