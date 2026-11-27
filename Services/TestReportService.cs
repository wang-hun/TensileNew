namespace TensileNeW.Services;

public sealed class TestReportService : IReportPdfExporter
{
    public void Export(ReportPdfExportRequest request) => ReportPdfAssembler.Export(request);
}
