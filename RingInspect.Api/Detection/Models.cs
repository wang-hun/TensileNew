using System.Text.Json;
using System.Text.Json.Serialization;

namespace RingInspect.Api.Detection;

/// <summary>宽松布尔：接受 true/false、1/0、"true"/"false"。</summary>
public sealed class FlexibleBoolConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True: return true;
            case JsonTokenType.False: return false;
            case JsonTokenType.Null: return null;
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var i) ? i != 0 : reader.GetDouble() != 0;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s)) return null;
                s = s.Trim();
                if (s is "1" or "true" or "TRUE" or "True" or "on" or "ON") return true;
                if (s is "0" or "false" or "FALSE" or "False" or "off" or "OFF") return false;
                throw new JsonException($"无法把 \"{s}\" 解析为布尔值");
            default:
                throw new JsonException("布尔值格式错误");
        }
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteBooleanValue(value.Value);
    }
}

public sealed class CircleDto
{
    public double Cx { get; set; }
    public double Cy { get; set; }

    /// <summary>外接圆半径。字段名保持网页版一致的大写 R。</summary>
    [JsonPropertyName("R")]
    public double R { get; set; }
}

public sealed class RingDto
{
    public int Area { get; set; }
    public double[] Bbox { get; set; } = new double[4];
}

public sealed class BandDto
{
    public double Gap { get; set; }
    public double Band { get; set; }
    public int Pixels { get; set; }
}

public sealed class ScaledDto
{
    public double Gap { get; set; }
    public double Band { get; set; }
    public double MinArea { get; set; }
    public double MinReach { get; set; }
    public double NotchDepth { get; set; }
    public double PairDist { get; set; }
}

public sealed class ProfileDto
{
    public int N { get; set; }
    public double[] R { get; set; } = Array.Empty<double>();
    public double[] RBody { get; set; } = Array.Empty<double>();
    public double[] Base { get; set; } = Array.Empty<double>();
    public double[] Dev { get; set; } = Array.Empty<double>();
    public double Sigma { get; set; }
}

public sealed class DefectDto
{
    public int? Id { get; set; }
    public string Type { get; set; } = "";
    public string TypeName { get; set; } = "";
    public double Angle { get; set; }
    public double[] Bbox { get; set; } = new double[4];
    public double X { get; set; }
    public double Y { get; set; }
    public double? DepthPx { get; set; }
    public double? DepthNorm { get; set; }
    public double? WidthDeg { get; set; }
    public double? ArcLenPx { get; set; }
    public double? AngFrom { get; set; }
    public double? AngTo { get; set; }
    public int? AreaPx { get; set; }
    public int[]? SizePx { get; set; }
    public int? MinLum { get; set; }
    public double? ReachPx { get; set; }
    public double? Elong { get; set; }
    public double? PcaLen { get; set; }
    public int? FreePts { get; set; }
    public int? SpikeCount { get; set; }
    public double[]? NotchBox { get; set; }
    public double[]? DebrisBox { get; set; }
    public List<double[]>? DebrisBoxes { get; set; }
    public double? GapPx { get; set; }
}

public sealed class AppliedParamDto
{
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "number";
    public object? Value { get; set; }
}

/// <summary>服务端耗时明细（毫秒）：读文件 / 解码 / 检测 / 底图编码 / 合计。</summary>
public sealed class TimingDto
{
    public double ReadMs { get; set; }
    public double DecodeMs { get; set; }
    public double DetectMs { get; set; }
    public double EncodeMs { get; set; }
    public double TotalMs { get; set; }
    public long FileBytes { get; set; }
    /// <summary>检测内部：亮度图 / 连通域 / 径向扫描 / 碎屑带 的耗时。</summary>
    public double LumMs { get; set; }
    public double LabelMs { get; set; }
    public double ScanMs { get; set; }
    public double BandMs { get; set; }
    /// <summary>缓冲清零 / 扫描线填充 细分。</summary>
    public double ClearMs { get; set; }
    public double FillMs { get; set; }
    /// <summary>暗色连通域个数。</summary>
    public int Components { get; set; }
}

public sealed class DetectResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
    public string? FileName { get; set; }
    public string? Verdict { get; set; }
    public double TimeMs { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public CircleDto? Circle { get; set; }
    public double Scale { get; set; }
    public RingDto? Ring { get; set; }
    public BandDto? Band { get; set; }
    public DetectParams? Params { get; set; }
    public ScaledDto? Scaled { get; set; }
    public List<DefectDto>? Defects { get; set; }
    public List<DefectDto>? Candidates { get; set; }
    public int DefectCount => Defects?.Count ?? 0;
    public int FilteredCount => Candidates?.Count ?? 0;
    public ProfileDto? Profile { get; set; }
    public string? Overlay { get; set; }
    /// <summary>路径模式专用：服务端生成的轻量 JPEG 底图（data URL）。默认不生成，需要时传 includeBaseImage=true。</summary>
    public string? BaseImage { get; set; }
    /// <summary>服务端耗时明细（读文件/解码/检测/编码）。</summary>
    public TimingDto? Timing { get; set; }
    public AppliedParamDto? Applied { get; set; }

    public static DetectResult Failure(string code, string message, double timeMs = 0) => new()
    {
        Ok = false,
        ErrorCode = code,
        Error = message,
        TimeMs = timeMs,
    };
}

public sealed class ApiError
{
    public bool Ok { get; set; } = false;
    public string ErrorCode { get; set; } = "";
    public string Error { get; set; } = "";
}
