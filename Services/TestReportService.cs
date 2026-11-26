using System.IO;

namespace TensileNeW.Services;

public sealed class TestReportService : IReportPdfExporter
{
    public void Export(ReportPdfExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        File.Copy(request.SourcePdfPath, request.OutputPdfPath, overwrite: true);
    }
}
