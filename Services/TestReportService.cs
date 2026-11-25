using System.IO;
using OfficeOpenXml;

namespace TensileNeW.Services;

public static class TestReportService
{
    private const string TemplateFileName = "杯凸测试报告模板 0 202060914.xlsx";
    private const string TemplateDirectoryName = "ReportTemplates";

    public static ExcelPackage CreateWorkbook()
    {
        string templatePath = Path.Combine(AppContext.BaseDirectory, TemplateDirectoryName, TemplateFileName);
        return new ExcelPackage(new FileInfo(templatePath));
    }

    public static ReportTemplateDocument ReadTemplate(ExcelPackage workbook)
    {
        return ReportTemplateDocumentReader.Read(workbook);
    }

    public static void SaveWorkbook(string fileName, ExcelPackage workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        workbook.SaveAs(new FileInfo(fileName));
    }
}
