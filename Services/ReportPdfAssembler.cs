using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using PdfiumViewer;

namespace TensileNeW.Services;

public static class ReportPdfAssembler
{
    private const float RenderDpi = 300f;
    private const double ReferenceWidth = 1240d;
    private const double ReferenceHeight = 1754d;

    public static void Export(ReportPdfExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(request.SourcePdfPath))
        {
            throw new FileNotFoundException("找不到报告空白模板。", request.SourcePdfPath);
        }

        using PdfDocument source = PdfDocument.Load(request.SourcePdfPath);
        var pageImages = new List<(byte[] Jpeg, double Width, double Height)>();
        for (int pageIndex = 0; pageIndex < source.PageCount; pageIndex++)
        {
            SizeF pageSize = source.PageSizes[pageIndex];
            int width = Math.Max(1, (int)Math.Ceiling(pageSize.Width * RenderDpi / 72d));
            int height = Math.Max(1, (int)Math.Ceiling(pageSize.Height * RenderDpi / 72d));
            using Image renderedPage = source.Render(pageIndex, width, height, RenderDpi, RenderDpi,
                PdfRenderFlags.Annotations | PdfRenderFlags.LcdText | PdfRenderFlags.ForPrinting);
            using Bitmap bitmap = new(renderedPage);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            DrawImages(graphics, bitmap.Size, pageIndex, request);
            foreach (ReportEditorField field in request.Fields.Where(field => field.PageIndex == pageIndex))
            {
                if (!request.Values.TryGetValue(field.Id, out string? value) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                float x = (float)(field.Left * bitmap.Width);
                float y = (float)(field.Top * bitmap.Height);
                float fieldWidth = (float)(field.Width * bitmap.Width);
                float fieldHeight = (float)(field.Height * bitmap.Height);
                float fontSize = (float)((field.FontSize ?? 11d) * RenderDpi / 96d);
                using var font = new Font("Microsoft YaHei", Math.Max(8f, fontSize), FontStyle.Regular, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Color.Black);
                var bounds = new RectangleF(x + 2, y + 1, Math.Max(1, fieldWidth - 4), Math.Max(1, fieldHeight - 2));
                using var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                graphics.DrawString(value, font, brush, bounds, format);
            }

            using var stream = new MemoryStream();
            SaveJpeg(bitmap, stream);
            pageImages.Add((stream.ToArray(), pageSize.Width, pageSize.Height));
        }

        WritePdf(request.OutputPdfPath, pageImages);
    }

    private static void DrawImages(Graphics graphics, Size bitmapSize, int pageIndex, ReportPdfExportRequest request)
    {
        DrawImage(request.ChartImagePng, pageIndex == 1, graphics,
            new RectangleF(bitmapSize.Width * 75f / (float)ReferenceWidth,
                bitmapSize.Height * 725f / (float)ReferenceHeight,
                bitmapSize.Width * 898f / (float)ReferenceWidth,
                bitmapSize.Height * 413f / (float)ReferenceHeight));
        DrawImage(request.CameraImagePng, pageIndex == 1, graphics,
            new RectangleF(bitmapSize.Width * 85f / (float)ReferenceWidth,
                bitmapSize.Height * 1175f / (float)ReferenceHeight,
                bitmapSize.Width * 628f / (float)ReferenceWidth,
                bitmapSize.Height * 310f / (float)ReferenceHeight));
    }

    private static void DrawImage(byte[]? bytes, bool pageMatches, Graphics graphics, RectangleF bounds)
    {
        if (!pageMatches || bytes is not { Length: > 0 })
        {
            return;
        }

        using var stream = new MemoryStream(bytes);
        using Image image = Image.FromStream(stream);
        float scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        float width = image.Width * scale;
        float height = image.Height * scale;
        graphics.DrawImage(image, bounds.X + (bounds.Width - width) / 2,
            bounds.Y + (bounds.Height - height) / 2, width, height);
    }

    private static void WritePdf(string outputPath, IReadOnlyList<(byte[] Jpeg, double Width, double Height)> pages)
    {
        int objectCount = 2 + pages.Count * 3;
        var objects = new byte[objectCount + 1][];
        objects[1] = Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>");
        var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(index => $"{3 + index * 3} 0 R"));
        objects[2] = Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");
        for (int index = 0; index < pages.Count; index++)
        {
            int pageObject = 3 + index * 3;
            int imageObject = pageObject + 1;
            int contentObject = pageObject + 2;
            (byte[] jpeg, double width, double height) = pages[index];
            string content = FormattableString.Invariant($"q {width:0.###} 0 0 {height:0.###} 0 0 cm /Im{index} Do Q");
            byte[] contentBytes = Encoding.ASCII.GetBytes(content);
            objects[pageObject] = Encoding.ASCII.GetBytes(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width.ToString("0.###", CultureInfo.InvariantCulture)} {height.ToString("0.###", CultureInfo.InvariantCulture)}] /Resources << /XObject << /Im{index} {imageObject} 0 R >> >> /Contents {contentObject} 0 R >>");
            objects[imageObject] = BuildStreamObject(
                $"<< /Type /XObject /Subtype /Image /Width {(int)Math.Ceiling(width * RenderDpi / 72d)} /Height {(int)Math.Ceiling(height * RenderDpi / 72d)} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>", jpeg);
            objects[contentObject] = BuildStreamObject($"<< /Length {contentBytes.Length} >>", contentBytes);
        }

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                WriteAscii(output, "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
                var offsets = new long[objectCount + 1];
                for (int objectNumber = 1; objectNumber <= objectCount; objectNumber++)
                {
                    offsets[objectNumber] = output.Position;
                    WriteAscii(output, $"{objectNumber} 0 obj\n");
                    output.Write(objects[objectNumber]);
                    WriteAscii(output, "\nendobj\n");
                }
                long xrefOffset = output.Position;
                WriteAscii(output, $"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
                for (int objectNumber = 1; objectNumber <= objectCount; objectNumber++)
                    WriteAscii(output, $"{offsets[objectNumber]:D10} 00000 n \n");
                WriteAscii(output, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
            }
            File.Move(temporaryPath, outputPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void SaveJpeg(Bitmap bitmap, Stream output)
    {
        ImageCodecInfo encoder = ImageCodecInfo.GetImageEncoders().First(item => item.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
        bitmap.Save(output, encoder, parameters);
    }
    private static byte[] BuildStreamObject(string dictionary, byte[] data)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, dictionary + "\nstream\n");
        stream.Write(data);
        WriteAscii(stream, "\nendstream");
        return stream.ToArray();
    }

    private static void WriteAscii(Stream stream, string text) =>
        stream.Write(Encoding.ASCII.GetBytes(text));
}