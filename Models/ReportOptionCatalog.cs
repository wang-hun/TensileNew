using System.Collections.Generic;

namespace TensileNeW.Models;

public sealed class ReportOptionCatalog
{
    public Dictionary<string, List<string>> Fields { get; set; } = new();

    public static IReadOnlyList<ReportOptionDefinition> Definitions { get; } =
    [
        new("Material", "材料", "F13", "模板注明两种可选材料。", ["22MnB5 AS150", "22MnB5"]),
        new("TestConclusion", "检测结论", "F38", "模板的 Pass / Fail 结论。", ["Pass", "Fail"]),
        new("Tester", "检测员", "D42", "模板现有人员，可按实际团队维护。", ["王国涛"]),
        new("Verifier", "审核", "H42", "模板现有人员，可按实际团队维护。", ["王雪斌"]),
        new("Authorizer", "批准", "K42", "模板现有人员，可按实际团队维护。", ["周力"]),
        new("ActualResult", "实际值", "K88", "模板中的焊缝开裂方向。", ["开裂垂直于焊缝"]),
        new("FinalJudgment", "最终判定", "B95", "模板现有判定，其他选项由用户添加。", ["合格"])
    ];

    public static ReportOptionCatalog CreateDefault()
    {
        var catalog = new ReportOptionCatalog();
        foreach (ReportOptionDefinition definition in Definitions)
        {
            catalog.Fields.Add(definition.Key, new List<string>(definition.DefaultOptions));
        }

        return catalog;
    }
}

public sealed record ReportOptionDefinition(
    string Key, string Title, string TemplateCell, string Description, IReadOnlyList<string> DefaultOptions);
