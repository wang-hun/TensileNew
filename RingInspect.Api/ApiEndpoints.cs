using System.Globalization;
using System.Text.Json;
using RingInspect.Api.Detection;

namespace RingInspect.Api;

public class DetectRequest
{
    /// <summary>图片 base64（可带 data:image/png;base64, 前缀）。</summary>
    public string? Image { get; set; }
    /// <summary>image 的别名。</summary>
    public string? ImageBase64 { get; set; }
    /// <summary>image 的别名。</summary>
    public string? Base64 { get; set; }
    public string? FileName { get; set; }
    public bool IncludeProfile { get; set; }
    public bool IncludeOverlay { get; set; }
    public DetectParamsInput? Params { get; set; }

    public string? GetImage() =>
        !string.IsNullOrWhiteSpace(Image) ? Image :
        !string.IsNullOrWhiteSpace(ImageBase64) ? ImageBase64 : Base64;
}

public sealed class ParamRequest : DetectRequest
{
    /// <summary>该接口对应的参数值；数字参数用 number，布尔参数可用 true/false 或 1/0。</summary>
    public JsonElement? Value { get; set; }
}

public sealed class BatchItem
{
    public string? Image { get; set; }
    public string? ImageBase64 { get; set; }
    public string? Base64 { get; set; }
    public string? FileName { get; set; }

    public string? GetImage() =>
        !string.IsNullOrWhiteSpace(Image) ? Image :
        !string.IsNullOrWhiteSpace(ImageBase64) ? ImageBase64 : Base64;
}

public sealed class BatchRequest
{
    public List<BatchItem>? Items { get; set; }
    public DetectParamsInput? Params { get; set; }
    public bool IncludeProfile { get; set; }
    public bool IncludeOverlay { get; set; }
}

public sealed class PixelRequest
{
    public string? Image { get; set; }
    public string? ImageBase64 { get; set; }
    public string? Base64 { get; set; }

    public string? GetImage() =>
        !string.IsNullOrWhiteSpace(Image) ? Image :
        !string.IsNullOrWhiteSpace(ImageBase64) ? ImageBase64 : Base64;
}

public static class ApiEndpoints
{
    private const int MaxBatchItems = 64;
    private static readonly RingInspector Inspector = new();

    public static void Map(WebApplication app)
    {
        app.MapGet("/", () => Results.Json(new
        {
            name = "圆环外缘缺陷检测 API",
            version = "1.0.0",
            note = "算法与网页版 detect.js V6 完全一致；图片以 base64 上传。",
            endpoints = new
            {
                health = "GET /api/health",
                paramDoc = "GET /api/params",
                strictPreset = "POST /api/detect/strict",
                custom = "POST /api/detect",
                batch = "POST /api/detect/batch",
                singleParam = "POST /api/detect/{参数名}（16 个参数各一个，见 GET /api/params）",
                debugPixels = "POST /api/debug/pixels",
            },
            docs = "见项目根目录 API接口文档.md",
        }));

        app.MapGet("/api/health", () => Results.Json(new
        {
            ok = true,
            service = "RingInspect.Api",
            version = "1.0.0",
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            targetFramework = "net8.0",
            serverTime = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            referenceRadius = RingInspector.RefR,
            profilePoints = RingInspector.Na,
        }));

        app.MapGet("/api/params", () => Results.Json(BuildParamDoc()));

        // 默认参数 + 严格模式（固定预设）
        app.MapPost("/api/detect/strict", (DetectRequest req) =>
        {
            var p = DetectParams.CreateDefault();
            p.StrictPair = true;
            return Execute(req.GetImage(), req.FileName, p, req.IncludeProfile, req.IncludeOverlay, null);
        });

        // 全参数自定义
        app.MapPost("/api/detect", (DetectRequest req) =>
        {
            if (!TryBuildParams(req.Params, out var p, out var err))
                return Error("INVALID_PARAM", err!);
            return Execute(req.GetImage(), req.FileName, p, req.IncludeProfile, req.IncludeOverlay, null);
        });

        // 批量
        app.MapPost("/api/detect/batch", (BatchRequest req) =>
        {
            if (req.Items == null || req.Items.Count == 0)
                return Error("EMPTY_BATCH", "items 不能为空");
            if (req.Items.Count > MaxBatchItems)
                return Error("BATCH_TOO_LARGE", $"一次最多 {MaxBatchItems} 张图片");
            if (!TryBuildParams(req.Params, out var p, out var err))
                return Error("INVALID_PARAM", err!);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = new List<DetectResult>(req.Items.Count);
            int ok = 0, ng = 0, bad = 0;
            foreach (var item in req.Items)
            {
                var image = item.GetImage();
                if (string.IsNullOrWhiteSpace(image))
                {
                    results.Add(DetectResult.Failure("MISSING_IMAGE", "缺少图片数据（image 字段，base64）") );
                    bad++;
                    continue;
                }
                DetectResult r;
                if (!ImageCodec.TryDecode(image, out var rgba, out var w, out var h, out var decErr))
                {
                    r = DetectResult.Failure("DECODE_FAILED", decErr ?? "图片解码失败");
                }
                else
                {
                    r = Inspector.Detect(rgba, w, h, p.Clone(), req.IncludeProfile, req.IncludeOverlay, item.FileName);
                }
                r.FileName = item.FileName;
                results.Add(r);
                if (!r.Ok) bad++;
                else if (r.Verdict == "NG") ng++;
                else ok++;
            }

            return Results.Json(new
            {
                total = results.Count,
                ok,
                ng,
                err = bad,
                elapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 3),
                results,
            });
        });

        // 单参数调控：/api/detect/{key}
        app.MapPost("/api/detect/{key}", (string key, ParamRequest req, HttpContext ctx) =>
            RunSingleParam(key, req, ctx));

        // 调试：返回解码后 RGBA 的 SHA-256，用来核对解码像素是否与浏览器一致
        app.MapPost("/api/debug/pixels", (PixelRequest req) =>
        {
            if (!ImageCodec.TryDecode(req.GetImage(), out var rgba, out var w, out var h, out var err))
                return Error("DECODE_FAILED", err ?? "图片解码失败");
            return Results.Json(new
            {
                ok = true,
                width = w,
                height = h,
                bytes = rgba.Length,
                sha256 = ImageCodec.Sha256Hex(rgba),
            });
        });
    }

    private static IResult RunSingleParam(string key, ParamRequest req, HttpContext ctx)
    {
        var spec = ParamRegistry.Find(key);
        if (spec == null)
        {
            return Results.Json(new ApiError
            {
                ErrorCode = "UNKNOWN_PARAM",
                Error = $"未知参数 “{key}”。可用参数：{string.Join(", ", ParamRegistry.All.Select(x => x.Key))}",
            }, statusCode: 404);
        }

        if (!TryBuildParams(req.Params, out var p, out var err))
            return Error("INVALID_PARAM", err!);

        JsonElement valueEl;
        if (req.Value is { } v && v.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            valueEl = v;
        }
        else if (ctx.Request.Query.TryGetValue("value", out var q) && q.Count > 0)
        {
            var raw = q[0];
            valueEl = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var dq)
                ? JsonSerializer.SerializeToElement(dq)
                : JsonSerializer.SerializeToElement(raw);
        }
        else
        {
            return Error("MISSING_VALUE", $"缺少 value：请在请求体传 {{ \"image\": \"...\", \"value\": ... }}");
        }

        object appliedValue;
        if (spec.Kind == "bool")
        {
            if (!TryReadBool(valueEl, out var b))
                return Error("INVALID_PARAM", $"{spec.Key} 需要布尔值（true/false 或 1/0）");
            spec.SetBool!(p, b);
            appliedValue = b;
        }
        else
        {
            if (!TryReadDouble(valueEl, out var d))
                return Error("INVALID_PARAM", $"{spec.Key} 需要数字");
            if (!Validate(spec, d, out var msg))
                return Error("INVALID_PARAM", msg!);
            spec.Set(p, d);
            appliedValue = d;
        }

        var applied = new AppliedParamDto { Key = spec.Key, Kind = spec.Kind, Value = appliedValue };
        return Execute(req.GetImage(), req.FileName, p, req.IncludeProfile, req.IncludeOverlay, applied);
    }

    private static IResult Execute(string? imageBase64, string? fileName, DetectParams p,
        bool includeProfile, bool includeOverlay, AppliedParamDto? applied)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return Error("MISSING_IMAGE", "缺少图片数据：请把图片转成 base64 放在 image 字段");

        if (!ImageCodec.TryDecode(imageBase64, out var rgba, out var w, out var h, out var decErr))
        {
            var fail = DetectResult.Failure("DECODE_FAILED", decErr ?? "图片解码失败");
            fail.FileName = fileName;
            fail.Params = p;
            return Results.Json(fail);
        }

        var result = Inspector.Detect(rgba, w, h, p, includeProfile, includeOverlay, fileName);
        result.Applied = applied;
        return Results.Json(result);
    }

    private static bool TryBuildParams(DetectParamsInput? input, out DetectParams p, out string? error)
    {
        p = DetectParams.CreateDefault();
        error = null;
        if (input == null) return true;

        foreach (var (key, value) in EnumerateOverrides(input))
        {
            var spec = ParamRegistry.Find(key)!;
            if (!Validate(spec, value, out error)) return false;
            spec.Set(p, value);
        }
        return true;
    }

    private static IEnumerable<(string Key, double Value)> EnumerateOverrides(DetectParamsInput i)
    {
        if (i.DarkTh.HasValue) yield return ("darkTh", i.DarkTh.Value);
        if (i.DebrisTh.HasValue) yield return ("debrisTh", i.DebrisTh.Value);
        if (i.Gap.HasValue) yield return ("gap", i.Gap.Value);
        if (i.Band.HasValue) yield return ("band", i.Band.Value);
        if (i.MinArea.HasValue) yield return ("minArea", i.MinArea.Value);
        if (i.MinReach.HasValue) yield return ("minReach", i.MinReach.Value);
        if (i.MinElong.HasValue) yield return ("minElong", i.MinElong.Value);
        if (i.NotchDepth.HasValue) yield return ("notchDepth", i.NotchDepth.Value);
        if (i.NotchMinWidth.HasValue) yield return ("notchMinWidth", i.NotchMinWidth.Value);
        if (i.BaselineWin.HasValue) yield return ("baselineWin", i.BaselineWin.Value);
        if (i.PairDist.HasValue) yield return ("pairDist", i.PairDist.Value);
        if (i.BigNotchDepth.HasValue) yield return ("bigNotchDepth", i.BigNotchDepth.Value);
        if (i.LoneDebrisNg.HasValue) yield return ("loneDebrisNG", i.LoneDebrisNg.Value ? 1 : 0);
        if (i.StrictPair.HasValue) yield return ("strictPair", i.StrictPair.Value ? 1 : 0);
        if (i.AngFrom.HasValue) yield return ("angFrom", i.AngFrom.Value);
        if (i.AngTo.HasValue) yield return ("angTo", i.AngTo.Value);
    }

    private static bool Validate(ParamSpec spec, double v, out string? error)
    {
        error = null;
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            error = $"{spec.Key} 不是有效数字";
            return false;
        }
        if (spec.Min.HasValue && v < spec.Min.Value)
        {
            error = $"{spec.Key} 不能小于 {spec.Min.Value.ToString(CultureInfo.InvariantCulture)}";
            return false;
        }
        if (spec.Max.HasValue && v > spec.Max.Value)
        {
            error = $"{spec.Key} 不能大于 {spec.Max.Value.ToString(CultureInfo.InvariantCulture)}";
            return false;
        }
        return true;
    }

    private static bool TryReadDouble(JsonElement el, out double value)
    {
        value = 0;
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                return el.TryGetDouble(out value);
            case JsonValueKind.String:
                return double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            default:
                return false;
        }
    }

    private static bool TryReadBool(JsonElement el, out bool value)
    {
        value = false;
        switch (el.ValueKind)
        {
            case JsonValueKind.True: value = true; return true;
            case JsonValueKind.False: value = false; return true;
            case JsonValueKind.Number:
                if (!el.TryGetDouble(out var d)) return false;
                value = d != 0;
                return true;
            case JsonValueKind.String:
                var s = el.GetString()?.Trim();
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1") { value = true; return true; }
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) || s == "0") { value = false; return true; }
                return false;
            default:
                return false;
        }
    }

    private static IResult Error(string code, string message, int status = 400) =>
        Results.Json(new ApiError { ErrorCode = code, Error = message }, statusCode: status);

    private static object BuildParamDoc() => new
    {
        referenceRadius = RingInspector.RefR,
        profilePoints = RingInspector.Na,
        note = "所有长度/面积参数均以“外半径 500px”为基准，检测时按实测半径自动换算。",
        defaults = DetectParams.CreateDefault(),
        endpoints = new
        {
            strictPreset = new { method = "POST", path = "/api/detect/strict", desc = "默认参数 + 严格模式" },
            custom = new { method = "POST", path = "/api/detect", desc = "全部参数可传" },
            batch = new { method = "POST", path = "/api/detect/batch", desc = "批量（最多 64 张）" },
            singleParam = new { method = "POST", path = "/api/detect/{key}", desc = "只改一个参数，其余用默认值" },
        },
        parameters = ParamRegistry.All.Select(s => new
        {
            key = s.Key,
            label = s.Label,
            kind = s.Kind,
            defaultValue = s.DefaultValue,
            min = s.Min,
            max = s.Max,
            step = s.Step,
            unit = s.Unit,
            hint = s.Hint,
            group = s.Group,
            endpoint = s.Endpoint,
        }),
    };
}
