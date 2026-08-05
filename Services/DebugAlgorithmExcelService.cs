using System.Globalization;
using System.IO;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using TensileNeW.Models;

namespace TensileNeW.Services;

public static class DebugAlgorithmExcelService
{
    public static string CreateDebugIntegratedDataFile(string sourceFileName, double displacementStep)
    {
        List<Loadmodel> source = ReadSourceData(sourceFileName);
        if (source.Count == 0)
        {
            throw new InvalidOperationException("未从原始数据 Excel 中读取到有效数据。");
        }

        string directory = Path.GetDirectoryName(sourceFileName) ?? string.Empty;
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(sourceFileName);
        string outputFileName = Path.Combine(directory, $"{nameWithoutExtension}_整合数据_debug.xlsx");

        DisplacementResamplingService.SaveResampledDataToFile(
            outputFileName,
            source,
            displacementStep);

        return outputFileName;
    }

    private static List<Loadmodel> ReadSourceData(string fileName)
    {
        using FileStream stream = File.OpenRead(fileName);
        IWorkbook workbook = CreateWorkbook(fileName, stream);
        ISheet sheet = workbook.GetSheetAt(0)
            ?? throw new InvalidOperationException("原始数据 Excel 没有可读取的工作表。");

        ColumnMap columns = ResolveColumns(sheet);
        List<Loadmodel> result = [];

        for (int rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            IRow? row = sheet.GetRow(rowIndex);
            if (row == null || IsHeaderRow(row, columns))
            {
                continue;
            }

            double? distance = GetNumericCell(row.GetCell(columns.Distance));
            double? force = GetNumericCell(row.GetCell(columns.Force));
            if (!distance.HasValue || !force.HasValue)
            {
                continue;
            }

            double press = GetNumericCell(row.GetCell(columns.Press)) ?? 0d;
            string time = GetCellText(row.GetCell(columns.Time));
            int index = GetIntCell(row.GetCell(columns.Index)) ?? result.Count + 1;

            result.Add(new Loadmodel
            {
                Index = index,
                RealPress = (float)press,
                RealDistance = (float)distance.Value,
                RealForce = (float)force.Value,
                Time = time
            });
        }

        return result;
    }

    private static IWorkbook CreateWorkbook(string fileName, Stream stream)
    {
        string extension = Path.GetExtension(fileName);
        if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return new XSSFWorkbook(stream);
        }

        if (string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            return new HSSFWorkbook(stream);
        }

        throw new NotSupportedException("只支持 .xlsx 和 .xls 原始数据文件。");
    }

    private static ColumnMap ResolveColumns(ISheet sheet)
    {
        for (int rowIndex = sheet.FirstRowNum; rowIndex <= Math.Min(sheet.LastRowNum, sheet.FirstRowNum + 10); rowIndex++)
        {
            IRow? row = sheet.GetRow(rowIndex);
            if (row == null)
            {
                continue;
            }

            Dictionary<string, int> headers = [];
            for (int column = row.FirstCellNum; column < row.LastCellNum; column++)
            {
                string key = NormalizeHeader(GetCellText(row.GetCell(column)));
                if (!string.IsNullOrEmpty(key) && !headers.ContainsKey(key))
                {
                    headers[key] = column;
                }
            }

            int? distance = FindHeader(headers, "位移", "realdistance", "distance", "displacement");
            int? force = FindHeader(headers, "力", "载荷", "拉伸力", "realforce", "force", "load");
            if (distance.HasValue && force.HasValue)
            {
                return new ColumnMap(
                    FindHeader(headers, "序号", "index") ?? 0,
                    FindHeader(headers, "压边", "压力", "realpress", "press") ?? 1,
                    distance.Value,
                    force.Value,
                    FindHeader(headers, "时间", "time") ?? 4);
            }
        }

        return new ColumnMap(0, 1, 2, 3, 4);
    }

    private static int? FindHeader(Dictionary<string, int> headers, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            string normalizedCandidate = NormalizeHeader(candidate);
            foreach ((string header, int column) in headers)
            {
                if (header.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                {
                    return column;
                }
            }
        }

        return null;
    }

    private static bool IsHeaderRow(IRow row, ColumnMap columns)
    {
        string distanceText = NormalizeHeader(GetCellText(row.GetCell(columns.Distance)));
        string forceText = NormalizeHeader(GetCellText(row.GetCell(columns.Force)));

        return distanceText.Contains("位移", StringComparison.OrdinalIgnoreCase) ||
               distanceText.Contains("distance", StringComparison.OrdinalIgnoreCase) ||
               forceText.Contains("力", StringComparison.OrdinalIgnoreCase) ||
               forceText.Contains("force", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHeader(string value)
    {
        return value
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Replace("（", string.Empty, StringComparison.Ordinal)
            .Replace("）", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private static int? GetIntCell(ICell? cell)
    {
        double? value = GetNumericCell(cell);
        return value.HasValue ? (int)Math.Round(value.Value, MidpointRounding.AwayFromZero) : null;
    }

    private static double? GetNumericCell(ICell? cell)
    {
        if (cell == null)
        {
            return null;
        }

        return cell.CellType switch
        {
            CellType.Numeric => cell.NumericCellValue,
            CellType.Formula when cell.CachedFormulaResultType == CellType.Numeric => cell.NumericCellValue,
            CellType.String => ParseDouble(cell.StringCellValue),
            CellType.Formula when cell.CachedFormulaResultType == CellType.String => ParseDouble(cell.StringCellValue),
            _ => ParseDouble(cell.ToString())
        };
    }

    private static double? ParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantValue))
        {
            return invariantValue;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double currentValue)
            ? currentValue
            : null;
    }

    private static string GetCellText(ICell? cell)
    {
        if (cell == null)
        {
            return string.Empty;
        }

        return cell.CellType switch
        {
            CellType.Numeric => cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
            CellType.Formula when cell.CachedFormulaResultType == CellType.Numeric => cell.NumericCellValue.ToString(CultureInfo.InvariantCulture),
            CellType.String => cell.StringCellValue?.Trim() ?? string.Empty,
            CellType.Formula when cell.CachedFormulaResultType == CellType.String => cell.StringCellValue?.Trim() ?? string.Empty,
            _ => cell.ToString()?.Trim() ?? string.Empty
        };
    }

    private sealed record ColumnMap(int Index, int Press, int Distance, int Force, int Time);
}
