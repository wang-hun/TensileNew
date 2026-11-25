using System.Collections.Generic;

namespace TensileNeW.Models;

public sealed class ReportOptionCatalog
{
    public Dictionary<string, List<string>> Fields { get; set; } = new();

    public static IReadOnlyList<ReportOptionDefinition> Definitions { get; } =
    [
        new("Material", "材料", "材料牌号。", ["22MnB5 AS150", "22MnB5"]),
        new("TestConclusion", "检测结论", "检测结论。", ["Pass", "Fail"]),
        new("Tester", "检测员", "检测人员。", ["王国涛"]),
        new("Verifier", "审核", "审核人员。", ["王雪斌"]),
        new("Authorizer", "批准", "批准人员。", ["周力"]),
        new("ActualResult", "实际值", "实际检测结果。", ["开裂垂直于焊缝"]),
        new("FinalJudgment", "最终判定", "最终判定。", ["合格"])
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
    string Key, string Title, string Description, IReadOnlyList<string> DefaultOptions);
