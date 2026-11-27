using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace TensileNeW.Services;

public static class ReportPdfAssembler
{
    private const double ReferenceWidth = 1240d;
    private const double ReferenceHeight = 1754d;

    static ReportPdfAssembler()
    {
        GlobalFontSettings.FallbackFontResolver = new ReportCjkFontResolver();
    }

    public static void Export(ReportPdfExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(request.SourcePdfPath))
        {
            throw new FileNotFoundException("找不到报告空白模板。", request.SourcePdfPath);
        }

        string? outputDirectory = Path.GetDirectoryName(request.OutputPdfPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string temporaryPath = request.OutputPdfPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (PdfDocument document = PdfReader.Open(request.SourcePdfPath, PdfDocumentOpenMode.Modify))
            {
                for (int pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
                {
                    PdfPage page = document.Pages[pageIndex];
                    using XGraphics graphics = XGraphics.FromPdfPage(
                        page, XGraphicsPdfPageOptions.Append, XGraphicsUnit.Point, XPageDirection.Downwards);
                    DrawImages(graphics, page, pageIndex, request);
                    DrawFields(graphics, page, pageIndex, request);
                }

                document.Save(temporaryPath);
            }

            File.Move(temporaryPath, request.OutputPdfPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void DrawFields(XGraphics graphics, PdfPage page, int pageIndex, ReportPdfExportRequest request)
    {
        foreach (ReportEditorField field in request.Fields.Where(field => field.PageIndex == pageIndex))
        {
            if (!request.Values.TryGetValue(field.Id, out string? value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var bounds = new XRect(
                field.Left * page.Width.Point + 2,
                field.Top * page.Height.Point + 1,
                Math.Max(1, field.Width * page.Width.Point - 4),
                Math.Max(1, field.Height * page.Height.Point - 2));
            var font = new XFont("Microsoft YaHei UI", Math.Max(6, (field.FontSize ?? 11d) * 72d / 96d));
            graphics.DrawString(value, font, XBrushes.Black, bounds,
                field.IsLeftAligned ? XStringFormats.CenterLeft : XStringFormats.Center);
        }
    }

    private static void DrawImages(XGraphics graphics, PdfPage page, int pageIndex, ReportPdfExportRequest request)
    {
        if (pageIndex != 1)
        {
            return;
        }

        DrawImage(request.ChartImagePng, graphics,
            new XRect(page.Width.Point * 75d / ReferenceWidth,
                page.Height.Point * 725d / ReferenceHeight,
                page.Width.Point * 898d / ReferenceWidth,
                page.Height.Point * 413d / ReferenceHeight));
        DrawImage(request.CameraImagePng, graphics,
            new XRect(page.Width.Point * 85d / ReferenceWidth,
                page.Height.Point * 1175d / ReferenceHeight,
                page.Width.Point * 628d / ReferenceWidth,
                page.Height.Point * 310d / ReferenceHeight));
    }

    private static void DrawImage(byte[]? bytes, XGraphics graphics, XRect bounds)
    {
        if (bytes is not { Length: > 0 })
        {
            return;
        }

        using var stream = new MemoryStream(bytes, writable: false);
        using XImage image = XImage.FromStream(stream);
        double scale = Math.Min(bounds.Width / image.PixelWidth, bounds.Height / image.PixelHeight);
        double width = image.PixelWidth * scale;
        double height = image.PixelHeight * scale;
        graphics.DrawImage(image,
            bounds.X + (bounds.Width - width) / 2,
            bounds.Y + (bounds.Height - height) / 2,
            width,
            height);
    }

    private sealed class ReportCjkFontResolver : IFontResolver
    {
        private const string FaceName = "ECSReportCjk";
        private readonly string? _fontPath = FindFontPath();

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (_fontPath is null || !familyName.Equals("Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new FontResolverInfo(FaceName, false, false);
        }

        public byte[]? GetFont(string faceName) =>
            faceName == FaceName && _fontPath is not null ? File.ReadAllBytes(_fontPath) : null;

        private static string? FindFontPath()
        {
            string fontsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            foreach (string fontName in new[] { "simhei.ttf", "deng.ttf", "simkai.ttf", "simfang.ttf" })
            {
                string path = Path.Combine(fontsDirectory, fontName);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }
    }
}