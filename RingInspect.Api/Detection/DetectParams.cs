using System.Text.Json.Serialization;

namespace RingInspect.Api.Detection;

/// <summary>
/// 检测参数。默认值与网页版 detect.js 的 DEFAULTS 完全一致。
/// 长度/面积类参数都以“外半径 500px”为参考基准，运行时会按实测半径自动换算。
/// </summary>
public sealed class DetectParams
{
    [JsonPropertyName("darkTh")] public double DarkTh { get; set; } = 128;
    [JsonPropertyName("debrisTh")] public double DebrisTh { get; set; } = 230;
    [JsonPropertyName("gap")] public double Gap { get; set; } = 3;
    [JsonPropertyName("band")] public double Band { get; set; } = 70;
    [JsonPropertyName("minArea")] public double MinArea { get; set; } = 14;
    [JsonPropertyName("minReach")] public double MinReach { get; set; } = 12;
    [JsonPropertyName("minElong")] public double MinElong { get; set; } = 2.2;
    [JsonPropertyName("notchDepth")] public double NotchDepth { get; set; } = 14;
    [JsonPropertyName("notchMinWidth")] public double NotchMinWidth { get; set; } = 1.0;
    [JsonPropertyName("baselineWin")] public double BaselineWin { get; set; } = 20;
    [JsonPropertyName("pairDist")] public double PairDist { get; set; } = 40;
    [JsonPropertyName("bigNotchDepth")] public double BigNotchDepth { get; set; } = 30;
    /// <summary>断裂最小弧长（按外半径 500px 计）：断口短于此值视为零件自带开口，不算断裂。</summary>
    [JsonPropertyName("breakMinWidth")] public double BreakMinWidth { get; set; } = 11;
    [JsonPropertyName("loneDebrisNG")] public bool LoneDebrisNg { get; set; } = true;
    [JsonPropertyName("strictPair")] public bool StrictPair { get; set; } = true;
    [JsonPropertyName("angFrom")] public double AngFrom { get; set; } = 0;
    [JsonPropertyName("angTo")] public double AngTo { get; set; } = 360;

    public DetectParams Clone() => (DetectParams)MemberwiseClone();

    /// <summary>网页版默认参数（严格模式）。</summary>
    public static DetectParams CreateDefault() => new();
}

/// <summary>
/// 请求里可选的参数覆盖（全部可空：没传的项沿用默认值）。
/// </summary>
public sealed class DetectParamsInput
{
    [JsonPropertyName("darkTh")] public double? DarkTh { get; set; }
    [JsonPropertyName("debrisTh")] public double? DebrisTh { get; set; }
    [JsonPropertyName("gap")] public double? Gap { get; set; }
    [JsonPropertyName("band")] public double? Band { get; set; }
    [JsonPropertyName("minArea")] public double? MinArea { get; set; }
    [JsonPropertyName("minReach")] public double? MinReach { get; set; }
    [JsonPropertyName("minElong")] public double? MinElong { get; set; }
    [JsonPropertyName("notchDepth")] public double? NotchDepth { get; set; }
    [JsonPropertyName("notchMinWidth")] public double? NotchMinWidth { get; set; }
    [JsonPropertyName("baselineWin")] public double? BaselineWin { get; set; }
    [JsonPropertyName("pairDist")] public double? PairDist { get; set; }
    [JsonPropertyName("bigNotchDepth")] public double? BigNotchDepth { get; set; }
    [JsonPropertyName("breakMinWidth")] public double? BreakMinWidth { get; set; }
    [JsonPropertyName("loneDebrisNG")] [JsonConverter(typeof(FlexibleBoolConverter))] public bool? LoneDebrisNg { get; set; }
    [JsonPropertyName("strictPair")] [JsonConverter(typeof(FlexibleBoolConverter))] public bool? StrictPair { get; set; }
    [JsonPropertyName("angFrom")] public double? AngFrom { get; set; }
    [JsonPropertyName("angTo")] public double? AngTo { get; set; }
}

public sealed class ParamSpec
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    /// <summary>number | bool</summary>
    public required string Kind { get; init; }
    public double? DefaultNumber { get; init; }
    public bool? DefaultBool { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    public string Unit { get; init; } = "";
    public string Hint { get; init; } = "";
    public string Group { get; init; } = "basic";
    public required Func<DetectParams, double> Get { get; init; }
    public required Action<DetectParams, double> Set { get; init; }
    public Func<DetectParams, bool>? GetBool { get; init; }
    public Action<DetectParams, bool>? SetBool { get; init; }

    public object? DefaultValue => Kind == "bool" ? (DefaultBool ?? false) : (DefaultNumber ?? 0);
    public string Endpoint => "/api/detect/" + Key;
}

/// <summary>参数注册表：与网页版左侧滑块一一对应（含取值范围与提示文案）。</summary>
public static class ParamRegistry
{
    public static readonly IReadOnlyList<ParamSpec> All = new List<ParamSpec>
    {
        new()
        {
            Key = "debrisTh", Label = "碎屑灰度阈值", Kind = "number", DefaultNumber = 240, Min = 60, Max = 250, Step = 1,
            Hint = "外缘外侧比它暗的像素算可疑。越大越敏感，背景脏时会误报",
            Get = p => p.DebrisTh, Set = (p, v) => p.DebrisTh = v,
        },
        new()
        {
            Key = "minArea", Label = "碎屑最小面积", Kind = "number", DefaultNumber = 14, Min = 2, Max = 300, Step = 1, Unit = "px²",
            Hint = "单位 px²，按外半径 500px 计，会随图像尺寸自动换算",
            Get = p => p.MinArea, Set = (p, v) => p.MinArea = v,
        },
        new()
        {
            Key = "minReach", Label = "尖端最小伸出长度", Kind = "number", DefaultNumber = 12, Min = 0, Max = 40, Step = 0.5, Unit = "px",
            Hint = "至少比圆环外缘扎出多少 px 才算尖端/异物（按外半径 500px 计），用来滤掉贴在圆环边缘的小齿尖和光晕",
            Get = p => p.MinReach, Set = (p, v) => p.MinReach = v,
        },
        new()
        {
            Key = "notchDepth", Label = "缺口最小深度", Kind = "number", DefaultNumber = 14, Min = 3, Max = 60, Step = 0.5, Unit = "px",
            Hint = "外缘比局部基线凹进多少 px 算缺口（按外半径 500px 计）；严格/宽松两种模式都用这个阈值找缺口",
            Get = p => p.NotchDepth, Set = (p, v) => p.NotchDepth = v,
        },
        new()
        {
            Key = "notchMinWidth", Label = "缺口最小宽度", Kind = "number", DefaultNumber = 1.0, Min = 0.25, Max = 10, Step = 0.25, Unit = "°",
            Hint = "单位：度",
            Get = p => p.NotchMinWidth, Set = (p, v) => p.NotchMinWidth = v,
        },
        new()
        {
            Key = "strictPair", Label = "严格模式：缺口必须依赖尖端才判缺陷", Kind = "bool", DefaultBool = true,
            Hint = "开启：只有“缺口+附近尖端”成对出现才报 NG，尖端不能单独算，大缺口也不能单独算；关闭：尖端可单独判缺陷，缺口达到下方深度阈值也可单独判缺陷",
            Get = p => p.StrictPair ? 1 : 0, Set = (p, v) => p.StrictPair = v != 0,
            GetBool = p => p.StrictPair, SetBool = (p, v) => p.StrictPair = v,
        },
        new()
        {
            Key = "loneDebrisNG", Label = "细长尖刺/明显异物单独判缺陷（不要求附近有缺口）", Kind = "bool", DefaultBool = true,
            Hint = "仅宽松模式生效；已用细长比与双侧亮背景过滤掉正常胖齿尖，开启可检出无缺口的独立细丝/尖刺",
            Get = p => p.LoneDebrisNg ? 1 : 0, Set = (p, v) => p.LoneDebrisNg = v != 0,
            GetBool = p => p.LoneDebrisNg, SetBool = (p, v) => p.LoneDebrisNg = v,
        },
        new()
        {
            Key = "pairDist", Label = "缺口-异物最大间距", Kind = "number", DefaultNumber = 40, Min = 5, Max = 120, Step = 1, Unit = "px",
            Hint = "缺口在此距离（px，按外半径 500px 计）内有异物才判缺陷",
            Get = p => p.PairDist, Set = (p, v) => p.PairDist = v,
        },
        new()
        {
            Key = "bigNotchDepth", Label = "大缺口独立判定深度", Kind = "number", DefaultNumber = 30, Min = 0, Max = 80, Step = 1, Unit = "px",
            Hint = "缺口深度达到此值（px，按外半径 500px 计）时，没有尖端也直接判缺陷；0 = 关闭；严格模式下自动失效",
            Get = p => p.BigNotchDepth, Set = (p, v) => p.BigNotchDepth = v,
        },
        new()
        {
            Key = "breakMinWidth", Label = "断裂最小弧长", Kind = "number", DefaultNumber = 11, Min = 2, Max = 200, Step = 1, Unit = "px",
            Hint = "断口弧长短于此值（px，按外半径 500px 计）当作零件自带开口，不判断裂；够长就判 NG",
            Get = p => p.BreakMinWidth, Set = (p, v) => p.BreakMinWidth = v,
        },
        new()
        {
            Key = "minElong", Label = "尖刺最小细长比", Kind = "number", DefaultNumber = 2.2, Min = 1.2, Max = 5, Step = 0.1,
            Hint = "连通块主轴长/宽至少达到此比值才算细长尖刺，用来滤除圆钝滚花齿", Group = "advanced",
            Get = p => p.MinElong, Set = (p, v) => p.MinElong = v,
        },
        new()
        {
            Key = "darkTh", Label = "零件暗阈值", Kind = "number", DefaultNumber = 128, Min = 40, Max = 220, Step = 1,
            Hint = "比它暗的像素当作零件本体", Group = "advanced",
            Get = p => p.DarkTh, Set = (p, v) => p.DarkTh = v,
        },
        new()
        {
            Key = "gap", Label = "检测带内边距", Kind = "number", DefaultNumber = 3, Min = 2, Max = 60, Step = 0.5, Unit = "px",
            Hint = "离外缘多远开始找碎屑，用来避开边缘本身的齿纹和模糊", Group = "advanced",
            Get = p => p.Gap, Set = (p, v) => p.Gap = v,
        },
        new()
        {
            Key = "band", Label = "检测带宽度", Kind = "number", DefaultNumber = 70, Min = 10, Max = 250, Step = 1, Unit = "px",
            Hint = "外缘向外多宽的范围内找碎屑/异物（按外半径 500px 计）", Group = "advanced",
            Get = p => p.Band, Set = (p, v) => p.Band = v,
        },
        new()
        {
            Key = "baselineWin", Label = "基线窗口", Kind = "number", DefaultNumber = 20, Min = 3, Max = 40, Step = 1, Unit = "°",
            Hint = "单位：度。外缘基线取前后这么多度的 65% 分位", Group = "advanced",
            Get = p => p.BaselineWin, Set = (p, v) => p.BaselineWin = v,
        },
        new()
        {
            Key = "angFrom", Label = "只看角度 从", Kind = "number", DefaultNumber = 0, Min = 0, Max = 360, Step = 1, Unit = "°",
            Hint = "0° 为正右方，顺时针增加，90° 为正下方；与“到”一起限制检测角度范围", Group = "advanced",
            Get = p => p.AngFrom, Set = (p, v) => p.AngFrom = v,
        },
        new()
        {
            Key = "angTo", Label = "只看角度 到", Kind = "number", DefaultNumber = 360, Min = 0, Max = 360, Step = 1, Unit = "°",
            Hint = "0° 为正右方，顺时针增加，90° 为正下方；angTo-angFrom>=360 表示整圈", Group = "advanced",
            Get = p => p.AngTo, Set = (p, v) => p.AngTo = v,
        },
    };

    public static ParamSpec? Find(string key) =>
        All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
}
