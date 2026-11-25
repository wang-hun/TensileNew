namespace TensileNeW.Services;

public enum ReportEditorFieldKind
{
    Text,
    DateTime,
    Dropdown
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
    public required ReportEditorFieldKind Kind { get; init; }
    public string? InitialValue { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];
    public bool IsRequired { get; init; }
}

public sealed class ReportEditorDocument
{
    public required string PdfPath { get; init; }
    public required IReadOnlyList<ReportEditorField> Fields { get; init; }
}

public sealed class ReportEditorResult
{
    public required IReadOnlyDictionary<string, string?> Values { get; init; }
}