using System.Drawing.Imaging;
using System.IO;
using OfficeOpenXml;
using OfficeOpenXml.Drawing;

namespace TensileNeW.Services;

public sealed class ReportTemplateDocument
{
    public required string Name { get; init; }
    public required IReadOnlyList<ReportTemplateSheet> Sheets { get; init; }
}

public sealed class ReportTemplateSheet
{
    public required string Name { get; init; }
    public required IReadOnlyList<double> ColumnWidths { get; init; }
    public required IReadOnlyList<double> RowHeights { get; init; }
    public required IReadOnlyList<ReportTemplateCell> Cells { get; init; }
    public required IReadOnlyList<ReportTemplateImage> Images { get; init; }
}

public sealed class ReportTemplateBorder
{
    public required double Thickness { get; init; }
    public required string? Color { get; init; }
}

public sealed class ReportTemplateCell
{
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required int RowSpan { get; init; }
    public required int ColumnSpan { get; init; }
    public required bool IsMergedAnchor { get; init; }
    public required string Text { get; init; }
    public required string? BackgroundColor { get; init; }
    public required string? ForegroundColor { get; init; }
    public required string? FontName { get; init; }
    public required double FontSize { get; init; }
    public required bool Bold { get; init; }
    public required bool Italic { get; init; }
    public required bool WrapText { get; init; }
    public required string HorizontalAlignment { get; init; }
    public required string VerticalAlignment { get; init; }
    public required ReportTemplateBorder TopBorder { get; init; }
    public required ReportTemplateBorder BottomBorder { get; init; }
    public required ReportTemplateBorder LeftBorder { get; init; }
    public required ReportTemplateBorder RightBorder { get; init; }
}

public sealed class ReportTemplateImage
{
    public required int Row { get; init; }
    public required int Column { get; init; }
    public required int RowSpan { get; init; }
    public required int ColumnSpan { get; init; }
    public required byte[] PngData { get; init; }
}

public static class ReportTemplateDocumentReader
{
    public static ReportTemplateDocument Read(ExcelPackage workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var sheets = new List<ReportTemplateSheet>();

        foreach (ExcelWorksheet worksheet in workbook.Workbook.Worksheets)
        {
            var dimension = worksheet.Dimension;
            int maxRow = dimension?.End.Row ?? 1;
            int maxColumn = dimension?.End.Column ?? 1;
            var mergedRanges = worksheet.MergedCells.Select(address => new ExcelAddress(address)).ToArray();
            var cells = new List<ReportTemplateCell>(maxRow * maxColumn);

            for (int row = 1; row <= maxRow; row++)
            {
                for (int column = 1; column <= maxColumn; column++)
                {
                    ExcelAddress? merged = mergedRanges.FirstOrDefault(range =>
                        row >= range.Start.Row && row <= range.End.Row &&
                        column >= range.Start.Column && column <= range.End.Column);
                    bool isMergedAnchor = merged is not null && row == merged.Start.Row && column == merged.Start.Column;

                    var source = worksheet.Cells[row, column];

                    var style = source.Style;
                    cells.Add(new ReportTemplateCell
                    {
                        Row = row - 1,
                        Column = column - 1,
                        RowSpan = isMergedAnchor ? merged!.End.Row - merged.Start.Row + 1 : 1,
                        ColumnSpan = isMergedAnchor ? merged!.End.Column - merged.Start.Column + 1 : 1,
                        IsMergedAnchor = isMergedAnchor,
                        Text = merged is not null && !isMergedAnchor ? string.Empty : source.Text ?? string.Empty,
                        BackgroundColor = GetFillColor(style.Fill.PatternType.ToString(), style.Fill.PatternColor.Rgb, style.Fill.BackgroundColor.Rgb),
                        ForegroundColor = GetColor(style.Font.Color.Rgb),
                        FontName = style.Font.Name,
                        FontSize = style.Font.Size,
                        Bold = style.Font.Bold,
                        Italic = style.Font.Italic,
                        WrapText = style.WrapText,
                        HorizontalAlignment = style.HorizontalAlignment.ToString(),
                        VerticalAlignment = style.VerticalAlignment.ToString(),
                        TopBorder = CreateBorder(style.Border.Top.Style.ToString(), style.Border.Top.Color.Rgb),
                        BottomBorder = CreateBorder(style.Border.Bottom.Style.ToString(), style.Border.Bottom.Color.Rgb),
                        LeftBorder = CreateBorder(style.Border.Left.Style.ToString(), style.Border.Left.Color.Rgb),
                        RightBorder = CreateBorder(style.Border.Right.Style.ToString(), style.Border.Right.Color.Rgb)
                    });
                }
            }

            var images = worksheet.Drawings.OfType<ExcelPicture>().Select(picture =>
            {
                using var stream = new MemoryStream();
                picture.Image.Save(stream, ImageFormat.Png);
                return new ReportTemplateImage
                {
                    Row = picture.From.Row,
                    Column = picture.From.Column,
                    RowSpan = Math.Max(1, picture.To.Row - picture.From.Row + 1),
                    ColumnSpan = Math.Max(1, picture.To.Column - picture.From.Column + 1),
                    PngData = stream.ToArray()
                };
            }).ToArray();

            sheets.Add(new ReportTemplateSheet
            {
                Name = worksheet.Name,
                ColumnWidths = Enumerable.Range(1, maxColumn)
                    .Select(column => worksheet.Column(column).Hidden
                        ? 0
                        : Math.Truncate((256 * worksheet.Column(column).Width + Math.Truncate(128d / 7)) / 256 * 7))
                    .ToArray(),
                RowHeights = Enumerable.Range(1, maxRow)
                    .Select(row => worksheet.Row(row).Hidden ? 0 : worksheet.Row(row).Height * 4 / 3)
                    .ToArray(),
                Cells = cells,
                Images = images
            });
        }

        return new ReportTemplateDocument
        {
            Name = "杯凸测试报告模板 0 202060914.xlsx",
            Sheets = sheets
        };
    }

    private static string? GetFillColor(string patternType, string? patternColor, string? backgroundColor)
    {
        return GetColor(patternType == "Solid" ? patternColor : backgroundColor);
    }

    private static ReportTemplateBorder CreateBorder(string style, string? color)
    {
        double thickness = style switch
        {
            "Hair" => 0.5,
            "Thin" or "Dotted" or "Dashed" or "DashDot" or "DashDotDot" or "SlantDashDot" => 0.75,
            "Medium" or "Double" => 1.5,
            "Thick" => 2,
            _ => 0
        };
        return new ReportTemplateBorder { Thickness = thickness, Color = GetColor(color) };
    }

    private static string? GetColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string hex = value.TrimStart('#');
        if (hex.Length == 8)
        {
            hex = hex[2..];
        }

        return hex.Length == 6 ? $"#{hex}" : null;
    }
}