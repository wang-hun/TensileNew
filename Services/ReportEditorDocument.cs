using TensileNeW.Models;

namespace TensileNeW.Services;

public enum ReportEditorFieldKind
{
    Text,
    DateTime,
    Dropdown,
    Date,
    ReadOnly
}

public sealed record ReportEditorField
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required int PageIndex { get; init; }
    public required double Left { get; init; }
    public required double Top { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public double? FontSize { get; init; }
    public required ReportEditorFieldKind Kind { get; init; }
    public string? InitialValue { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];
    public bool IsRequired { get; init; }
}

public sealed class ReportEditorDocument
{
    public required string PdfPath { get; init; }
    public required IReadOnlyList<ReportEditorField> Fields { get; init; }
    public byte[]? ChartImagePng { get; init; }
    public byte[]? CameraImagePng { get; init; }
}

public sealed class ReportEditorResult
{
    public required IReadOnlyDictionary<string, string?> Values { get; init; }
}

public sealed class ReportPdfExportRequest
{
    public required string SourcePdfPath { get; init; }
    public required string OutputPdfPath { get; init; }
}

public static class ReportTemplateFieldCatalog
{
    private const double ReferenceWidth = 1240d;
    private const double ReferenceHeight = 1754d;

    public static IReadOnlyList<ReportEditorField> CreateBlankReportFields(ReportOptionCatalog options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return
        [
            Field("Quantity", "样品数量", 0, 540, 385, 330, 32),
            Field("Material", "材料", 0, 525, 440, 335, 32, ReportEditorFieldKind.Dropdown, options),
            Field("SampleDescription", "样品描述", 0, 535, 494, 430, 32, initialValue: "TRB 拼焊型板"),
            Field("ProjectNo", "项目号", 0, 535, 550, 365, 32),
            Field("SampleDate", "收样日期", 0, 548, 622, 182, 40, ReportEditorFieldKind.Date),
            Field("TestDate", "检测日期", 0, 548, 677, 182, 40, ReportEditorFieldKind.Date),
            Field("ReportDate", "报告日期", 0, 548, 732, 182, 40, ReportEditorFieldKind.Date),
            Field("Temperature", "环境温度（℃）", 0, 668, 839, 68, 23, fontSize: 9.5),
            Field("Humidity", "环境湿度（%RH）", 0, 800, 839, 61, 23, fontSize: 9.5),
            Field("EvaluationBasis", "评价依据", 0, 540, 1086, 385, 32),
            Field("TestConclusion", "检测结论", 0, 535, 1141, 310, 32, ReportEditorFieldKind.Dropdown, options),
            Field("Tester", "检测员", 0, 205, 1308, 130, 24, ReportEditorFieldKind.Dropdown, options, fontSize: 9.5),
            Field("Verifier", "审核", 0, 586, 1308, 138, 24, ReportEditorFieldKind.Dropdown, options, fontSize: 9.5),
            Field("Authorizer", "批准", 0, 969, 1308, 141, 24, ReportEditorFieldKind.Dropdown, options, fontSize: 9.5),
            Field("TesterDate", "检测员日期", 0, 205, 1355, 130, 24, ReportEditorFieldKind.Date, fontSize: 9.5),
            Field("VerifierDate", "审核日期", 0, 586, 1355, 138, 24, ReportEditorFieldKind.Date, fontSize: 9.5),
            Field("AuthorizerDate", "批准日期", 0, 969, 1355, 141, 24, ReportEditorFieldKind.Date, fontSize: 9.5),
            Field("MaximumForce", "最大拉伸力（kN）", 1, 988, 884, 170, 24, ReportEditorFieldKind.ReadOnly, fontSize: 9.5),
            Field("EffectiveDisplacement", "有效拉伸位移（mm）", 1, 988, 994, 170, 24, ReportEditorFieldKind.ReadOnly, fontSize: 9.5),
            Field("ActualResult", "实际值", 1, 988, 1315, 170, 32, ReportEditorFieldKind.Dropdown, options),
            Field("FinalJudgment", "最终判定", 1, 153, 1500, 200, 24, ReportEditorFieldKind.Dropdown, options, fontSize: 9)
        ];
    }

    private static ReportEditorField Field(
        string id, string label, int pageIndex, double left, double top, double width, double height,
        ReportEditorFieldKind kind = ReportEditorFieldKind.Text, ReportOptionCatalog? options = null,
        string? initialValue = null, double? fontSize = null)
    {
        return new ReportEditorField
        {
            Id = id,
            Label = label,
            PageIndex = pageIndex,
            Left = left / ReferenceWidth,
            Top = top / ReferenceHeight,
            Width = width / ReferenceWidth,
            Height = height / ReferenceHeight,
            FontSize = fontSize,
            Kind = kind,
            InitialValue = initialValue,
            Options = kind == ReportEditorFieldKind.Dropdown && options!.Fields.TryGetValue(id, out var choices)
                ? choices : [],
            IsRequired = true
        };
    }
}
