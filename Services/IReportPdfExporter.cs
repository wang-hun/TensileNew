namespace TensileNeW.Services;

public interface IReportPdfExporter
{
    void Export(ReportPdfExportRequest request);
}