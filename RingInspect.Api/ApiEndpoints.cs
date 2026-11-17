using System.IO;
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

/// <summary>POST /api/switch 的请求体。省略 enabled 时表示取反。</summary>
public sealed class SwitchRequest
{
    /// <summary>true = 允许检测，false = 暂停检测；不传则取反当前状态。</summary>
    public bool? Enabled { get; set; }
}

/// <summary>本地图片路径接口：只传路径，服务端自己读取解码。</summary>
public sealed class PathRequest
{
    /// <summary>单个图片路径（绝对路径或相对 RINGINSPECT_ROOT 的路径）。path 的别名。</summary>
    public string? Path { get; set; }
    public string? File { get; set; }
    /// <summary>批量路径。</summary>
    public List<string>? Paths { get; set; }
    public bool IncludeProfile { get; set; }
    public bool IncludeOverlay { get; set; }
    /// <summary>是否返回 JPEG 底图（默认不返回：大图编码很贵，前端一般直接用原图画框）。</summary>
    public bool IncludeBaseImage { get; set; }
    public DetectParamsInput? Params { get; set; }
    /// <summary>严格模式（默认 true）。</summary>
    public bool? Strict { get; set; }

    public string? GetPath() => !string.IsNullOrWhiteSpace(Path) ? Path : File;
}

public static class ApiEndpoints
{
    private const int MaxBatchItems = 64;
    private static readonly RingInspector Inspector = new();

    /// <summary>
    /// 严格模式用的参数预设。默认 = 默认参数 + 严格模式；
    /// 宿主（如 WPF 程序）可以在启动时替换成用户保存/界面编辑的预设。
    /// </summary>
    public static Func<DetectParams> StrictPreset { get; set; } = DefaultStrictPreset;

    public static DetectParams DefaultStrictPreset()
    {
        var p = DetectParams.CreateDefault();
        p.StrictPair = true;
        return p;
    }

    // ===== 桌面端（WPF）联机运行时用的钩子，API 独立部署时都不设置 =====

    /// <summary>可编程指定允许根目录（优先于环境变量 RINGINSPECT_ROOT）。</summary>
    public static string? RootOverride { get; set; }

    /// <summary>内嵌到桌面程序时置 true：本机可信，不再限制根目录，任意绝对路径都能读。</summary>
    public static bool AllowAnyPath { get; set; }

    /// <summary>相对路径的基准目录（桌面端填「默认检测文件夹」）。</summary>
    public static string? RelativeBasePath { get; set; }

    /// <summary>极速接口没给路径时的兜底（桌面端用界面里配的默认文件夹 + 文件名）。</summary>
    public static Func<string?>? DefaultImagePath { get; set; }

    /// <summary>联机武装状态：上位机发 start 之后才允许受理检测请求；为空表示不限制。</summary>
    public static Func<bool>? IsArmed { get; set; }

    /// <summary>极速接口每次检测结束后的回调（桌面端据此回 stop/continue 并上报上位机）。</summary>
    public static Action<FastOutcome>? OnFastResult { get; set; }

    /// <summary>一次极速检测的结果摘要（给宿主做联机联动用）。</summary>
    public sealed class FastOutcome
    {
        public string? FullPath { get; set; }
        public string? FileName { get; set; }
        public bool Ok { get; set; }
        public string? Verdict { get; set; }
        public string? ErrorCode { get; set; }
        public DetectResult? Result { get; set; }
        public double ElapsedMs { get; set; }
    }


    // 本地路径接口只允许读取此根目录下的图片，防止目录穿越。
    // 由部署方通过环境变量 RINGINSPECT_ROOT 配置；未配置时路径模式不可用（避免内置任何本机路径）。
    private static string? AllowedRoot
    {
        get
        {
            if (RootOverride != null)
            {
                var r = RootOverride.Trim();
                if (r.Length == 0) return null;
                try { return Path.GetFullPath(r); } catch { return null; }
            }
            var v = Environment.GetEnvironmentVariable("RINGINSPECT_ROOT");
            if (string.IsNullOrWhiteSpace(v)) return null;
            try { return Path.GetFullPath(v.Trim()); } catch { return null; }
        }
    }

    private static bool TryResolvePath(string? input, out string fullPath, out string? error)
    {
        fullPath = "";
        error = null;
        string? rootCfg = AllowedRoot;
        if (rootCfg == null && !AllowAnyPath)
        {
            error = "服务端未配置允许根目录（环境变量 RINGINSPECT_ROOT），路径模式不可用";
            return false;
        }
        if (string.IsNullOrWhiteSpace(input)) { error = "缺少 path 字段"; return false; }
        try
        {
            string root = rootCfg ?? (string.IsNullOrWhiteSpace(RelativeBasePath) ? "" : Path.GetFullPath(RelativeBasePath!));
            string p = Path.IsPathRooted(input) ? Path.GetFullPath(input) : Path.GetFullPath(Path.Combine(root, input));
            // 必须落在允许的根目录内（错误信息不回显服务端真实路径）；桌面端内嵌运行时可放开
            if (!AllowAnyPath && rootCfg != null &&
                (!p.StartsWith(rootCfg, StringComparison.OrdinalIgnoreCase) ||
                 (p.Length > rootCfg.Length && p[rootCfg.Length] != Path.DirectorySeparatorChar && p[rootCfg.Length] != Path.AltDirectorySeparatorChar)))
            {
                error = "路径不在允许的根目录内";
                return false;
            }
            // 只回显文件名，不回显完整路径
            if (!File.Exists(p)) { error = "文件不存在：" + Path.GetFileName(p); return false; }
            string ext = Path.GetExtension(p).ToLowerInvariant();
            if (ext is not (".bmp" or ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".gif" or ".webp"))
            {
                error = "不支持的图片格式：" + ext;
                return false;
            }
            fullPath = p;
            return true;
        }
        catch (Exception) { error = "路径无效"; return false; }
    }

    private static DetectParams BuildPathParams(PathRequest req, out string? err)
    {
        if (!TryBuildParams(req.Params, out var p, out err)) return DetectParams.CreateDefault();
        p.StrictPair = req.Strict ?? true;   // 路径接口默认严格模式
        return p;
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/", () => Results.Json(new
        {
            name = "圆环外缘缺陷检测 API",
            version = "1.0.0",
            note = "算法与网页版 detect.js V6 完全一致；支持 base64 上传与本地路径两种模式（路径模式服务端自行读图）。",
            endpoints = new
            {
                health = "GET /api/health",
                paramDoc = "GET /api/params",
                strictPreset = "POST /api/detect/strict",
            detectionSwitch = "GET /api/switch · POST /api/switch {\"enabled\":true|false}（与桌面界面共用同一开关）",
                custom = "POST /api/detect",
                batch = "POST /api/detect/batch",
                singleParam = "POST /api/detect/{参数名}（16 个参数各一个，见 GET /api/params）",
                debugPixels = "POST /api/debug/pixels",
                pathStrict = "POST /api/detect/path/strict",
                pathCustom = "POST /api/detect/path",
                pathBatch = "POST /api/detect/path/batch",
                blobStrict = "POST /api/detect/blob/strict（body 为图片原始字节，参数走 query）",
                blobCustom = "POST /api/detect/blob",
                allowedRoot = AllowedRoot ?? "(未配置)",
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
            detectionEnabled = DetectionSwitch.Enabled,
        }));

        app.MapGet("/api/params", () => Results.Json(BuildParamDoc()));

        // ---- 检测启停开关：桌面界面上的开关和 HTTP 接口操作同一份状态 ----
        app.MapGet("/api/switch", () => Results.Json(new
        {
            ok = true,
            enabled = DetectionSwitch.Enabled,
            state = DetectionSwitch.State,
        }));

        app.MapPost("/api/switch", (SwitchRequest? req) =>
        {
            bool target = req?.Enabled ?? !DetectionSwitch.Enabled;
            DetectionSwitch.Set(target);
            return Results.Json(new
            {
                ok = true,
                enabled = DetectionSwitch.Enabled,
                state = DetectionSwitch.State,
            });
        });

        // ===== 本地路径接口：传路径，服务端自己读文件解码（不经过 base64） =====
        app.MapPost("/api/detect/path/strict", (PathRequest req) =>
        {
            if (Paused() is { } paused) return paused;
            if (!TryResolvePath(req.GetPath(), out var full, out var err)) return Error("BAD_PATH", err!);
            var p = StrictPreset().Clone();
            p.StrictPair = req.Strict ?? true;
            return ExecutePath(full, p, req.IncludeProfile, req.IncludeOverlay, req.IncludeBaseImage, null);
        });

        app.MapPost("/api/detect/path", (PathRequest req) =>
        {
            if (Paused() is { } paused) return paused;
            if (!TryResolvePath(req.GetPath(), out var full, out var perr)) return Error("BAD_PATH", perr!);
            var p = BuildPathParams(req, out var err);
            if (err != null) return Error("INVALID_PARAM", err);
            return ExecutePath(full, p, req.IncludeProfile, req.IncludeOverlay, req.IncludeBaseImage, null);
        });

        // ===== 极速接口：只传图片路径，固定严格预设，最小开销（GET/POST 均可） =====
        // 联机模式：没给 path 时回退到宿主配置的默认文件；未收到 start 之前一律 409。
        app.MapGet("/api/detect/fast", async (HttpRequest http) =>
        {
            if (Paused() is { } paused) return paused;
            if (NotArmed() is { } armed) return armed;
            var raw = http.Query["path"].ToString();
            if (string.IsNullOrWhiteSpace(raw)) raw = DefaultImagePath?.Invoke() ?? "";
            if (!TryResolvePath(raw, out var full, out var err)) return Error("BAD_PATH", err!);
            return await ExecuteFastAsync(full);
        });

        app.MapPost("/api/detect/fast", async (PathRequest? req) =>
        {
            if (Paused() is { } paused) return paused;
            if (NotArmed() is { } armed) return armed;
            var raw = req?.GetPath();
            if (string.IsNullOrWhiteSpace(raw)) raw = DefaultImagePath?.Invoke() ?? "";
            if (!TryResolvePath(raw, out var full, out var err)) return Error("BAD_PATH", err!);
            return await ExecuteFastAsync(full);
        });

        app.MapPost("/api/detect/path/batch", (PathRequest req) =>
        {
            if (Paused() is { } paused) return paused;
            var paths = req.Paths ?? new List<string>();
            if (paths.Count == 0) return Error("EMPTY_BATCH", "paths 不能为空");
            if (paths.Count > MaxBatchItems) return Error("BATCH_TOO_LARGE", $"一次最多 {MaxBatchItems} 张图片");
            var p = BuildPathParams(req, out var err);
            if (err != null) return Error("INVALID_PARAM", err);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = new List<DetectResult>(paths.Count);
            int ok = 0, ng = 0, bad = 0;
            foreach (var raw in paths)
            {
                if (!TryResolvePath(raw, out var full, out var perr))
                {
                    results.Add(DetectResult.Failure("BAD_PATH", perr ?? "路径无效")); bad++; continue;
                }
                var swItem = System.Diagnostics.Stopwatch.StartNew();
                byte[] bytes;
                try { bytes = File.ReadAllBytes(full); }
                catch (Exception) { results.Add(DetectResult.Failure("READ_FAILED", "读取文件失败")); bad++; continue; }
                double readMs = swItem.Elapsed.TotalMilliseconds, decodeMs = 0, encodeMs = 0;
                DetectResult r;
                swItem.Restart();
                if (!ImageCodec.TryDecodeBytes(bytes, out var rgba, out var w, out var h, out var decErr))
                    r = DetectResult.Failure("DECODE_FAILED", decErr ?? "图片解码失败");
                else
                {
                    decodeMs = swItem.Elapsed.TotalMilliseconds;
                    swItem.Restart();
                    r = Inspector.Detect(rgba, w, h, p.Clone(), req.IncludeProfile, req.IncludeOverlay, Path.GetFileName(full));
                    if (r.Timing != null) r.Timing.DetectMs = Math.Round(swItem.Elapsed.TotalMilliseconds, 2);
                    if (req.IncludeBaseImage)
                    {
                        var swEnc = System.Diagnostics.Stopwatch.StartNew();
                        r.BaseImage = ImageCodec.ToJpegDataUrl(rgba, w, h);
                        encodeMs = swEnc.Elapsed.TotalMilliseconds;
                    }
                }
                if (r.Timing != null)
                {
                    r.Timing.ReadMs = Math.Round(readMs, 2);
                    r.Timing.DecodeMs = Math.Round(decodeMs, 2);
                    r.Timing.EncodeMs = Math.Round(encodeMs, 2);
                    r.Timing.FileBytes = bytes?.Length ?? 0;
                    r.Timing.TotalMs = Math.Round(readMs + decodeMs + r.Timing.DetectMs + encodeMs, 2);
                }
                r.FileName = Path.GetFileName(full);
                results.Add(r);
                if (!r.Ok) bad++; else if (r.Verdict == "NG") ng++; else ok++;
            }
            return Results.Json(new { total = results.Count, ok, ng, err = bad,
                elapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 3), results });
        });

        // ===== blob 接口：图片原始字节直接放 body（不做 base64），参数走 query string =====
        app.MapPost("/api/detect/blob/strict", async (HttpRequest http) =>
        {
            if (Paused() is { } paused) return paused;
            var (bytes, readMs) = await ReadBodyBytesAsync(http);
            if (bytes == null) return Error("MISSING_IMAGE", "请求体为空：请把图片原始字节放在 body");
            var p = StrictPreset().Clone();
            p.StrictPair = QueryBool(http, "strict", true);
            return ExecuteBytes(bytes, http.Query["fileName"], p,
                QueryBool(http, "includeProfile", false), QueryBool(http, "includeOverlay", false), null,
                QueryBool(http, "includeBaseImage", false), readMs);
        });

        app.MapPost("/api/detect/blob", async (HttpRequest http) =>
        {
            if (Paused() is { } paused) return paused;
            var (bytes, readMs) = await ReadBodyBytesAsync(http);
            if (bytes == null) return Error("MISSING_IMAGE", "请求体为空：请把图片原始字节放在 body");
            if (!TryApplyQueryParams(http, out var p, out var perr)) return Error("INVALID_PARAM", perr!);
            p.StrictPair = QueryBool(http, "strict", p.StrictPair);
            return ExecuteBytes(bytes, http.Query["fileName"], p,
                QueryBool(http, "includeProfile", false), QueryBool(http, "includeOverlay", false), null,
                QueryBool(http, "includeBaseImage", false), readMs);
        });

        // 默认参数 + 严格模式（固定预设）
        app.MapPost("/api/detect/strict", (DetectRequest req) =>
        {
            if (Paused() is { } paused) return paused;
            var p = StrictPreset().Clone();
            p.StrictPair = true;   // 严格模式恒开
            return Execute(req.GetImage(), req.FileName, p, req.IncludeProfile, req.IncludeOverlay, null);
        });

        // 全参数自定义
        app.MapPost("/api/detect", (DetectRequest req) =>
        {
            if (Paused() is { } paused) return paused;
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

        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (!ImageCodec.TryDecode(imageBase64, out var rgba, out var w, out var h, out var decErr))
        {
            var fail = DetectResult.Failure("DECODE_FAILED", decErr ?? "图片解码失败");
            fail.FileName = fileName;
            fail.Params = p;
            return Results.Json(fail);
        }
        var decodeMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var result = RunDetect(rgba, w, h, p, includeProfile, includeOverlay, fileName, applied);
        var detectMs = sw.Elapsed.TotalMilliseconds;
        var timing = result.Timing ?? new TimingDto();
        result.Timing = timing;
        timing.ReadMs = 0;                       // base64 模式没有服务端读文件这一步
        timing.DecodeMs = Math.Round(decodeMs, 2);   // 含 base64 还原 + 图片解码
        timing.DetectMs = Math.Round(detectMs, 2);
        timing.EncodeMs = 0;
        timing.TotalMs = Math.Round(decodeMs + detectMs, 2);
        timing.FileBytes = 0;
        return Results.Json(result);
    }

    private static IResult ExecuteBytes(byte[] bytes, string? fileName, DetectParams p,
        bool includeProfile, bool includeOverlay, AppliedParamDto? applied, bool includeBaseImage = false,
        double readMs = 0)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (!ImageCodec.TryDecodeBytes(bytes, out var rgba, out var w, out var h, out var decErr))
        {
            var fail = DetectResult.Failure("DECODE_FAILED", decErr ?? "图片解码失败");
            fail.FileName = fileName;
            fail.Params = p;
            return Results.Json(fail);
        }
        var decodeMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var result = RunDetect(rgba, w, h, p, includeProfile, includeOverlay, fileName, applied);
        var detectMs = sw.Elapsed.TotalMilliseconds;
        double encodeMs = 0;
        if (includeBaseImage)
        {
            sw.Restart();
            result.BaseImage = ImageCodec.ToJpegDataUrl(rgba, w, h);
            encodeMs = sw.Elapsed.TotalMilliseconds;
        }
        var timing = result.Timing ?? new TimingDto();
        result.Timing = timing;
        timing.ReadMs = Math.Round(readMs, 2);        // blob 模式：收取请求体的耗时
        timing.DecodeMs = Math.Round(decodeMs, 2);
        timing.DetectMs = Math.Round(detectMs, 2);
        timing.EncodeMs = Math.Round(encodeMs, 2);
        timing.TotalMs = Math.Round(readMs + decodeMs + detectMs + encodeMs, 2);
        timing.FileBytes = bytes.Length;
        return Results.Json(result);
    }

    /// <summary>路径模式：自己读文件 → 解码 → 检测，顺带返回分段耗时。</summary>
    private static IResult ExecutePath(string full, DetectParams p, bool includeProfile, bool includeOverlay,
        bool includeBaseImage, AppliedParamDto? applied)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        byte[] bytes;
        try { bytes = File.ReadAllBytes(full); }
        catch (Exception) { return Error("READ_FAILED", "读取文件失败"); }
        var readMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        if (!ImageCodec.TryDecodeBytes(bytes, out var rgba, out var w, out var h, out var decErr))
        {
            var fail = DetectResult.Failure("DECODE_FAILED", decErr ?? "图片解码失败");
            fail.FileName = Path.GetFileName(full);
            fail.Params = p;
            return Results.Json(fail);
        }
        var decodeMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var result = RunDetect(rgba, w, h, p, includeProfile, includeOverlay, Path.GetFileName(full), applied);
        var detectMs = sw.Elapsed.TotalMilliseconds;
        double encodeMs = 0;
        if (includeBaseImage)
        {
            sw.Restart();
            result.BaseImage = ImageCodec.ToJpegDataUrl(rgba, w, h);
            encodeMs = sw.Elapsed.TotalMilliseconds;
        }
        var timing = result.Timing ?? new TimingDto();
        result.Timing = timing;
        timing.ReadMs = Math.Round(readMs, 2);
        timing.DecodeMs = Math.Round(decodeMs, 2);
        timing.DetectMs = Math.Round(detectMs, 2);
        timing.EncodeMs = Math.Round(encodeMs, 2);
        timing.TotalMs = Math.Round(readMs + decodeMs + detectMs + encodeMs, 2);
        timing.FileBytes = bytes.Length;
        return Results.Json(result);
    }

    /// <summary>
    /// 极速单图检测：只按路径读一张图，用固定严格预设，不生成剖线/底图。
    /// 与 /api/detect/path/strict（includeProfile=false、includeOverlay=false）结果逐字段一致，
    /// 只是把请求体压到最小（只要路径）并把读文件换成未清零直读。
    /// </summary>
    private static async Task<IResult> ExecuteFastAsync(string full)
    {
        var swAll = System.Diagnostics.Stopwatch.StartNew();
        for (int attempt = 1; ; attempt++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!TryReadStable(full, out var bytes, out var rerr))
            {
                if (attempt < FastAttempts) { await Task.Delay(FastRetryDelayMs); continue; }
                var failRead = DetectResult.Failure("READ_FAILED", rerr ?? "读取文件失败");
                failRead.FileName = Path.GetFileName(full);
                NotifyFast(full, failRead, false, "READ_FAILED", swAll.Elapsed.TotalMilliseconds);
                return Error("READ_FAILED", rerr ?? "读取文件失败");
            }
            var readMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            if (!ImageCodec.TryDecodeBytes(bytes, out var rgba, out var w, out var h, out var decErr))
            {
                // 上位机常常“边写边让我们读”，读到半个文件就重试，不改变任何检测逻辑
                if (attempt < FastAttempts) { await Task.Delay(FastRetryDelayMs); continue; }
                var fail = DetectResult.Failure("DECODE_FAILED", decErr ?? "图片解码失败");
                fail.FileName = Path.GetFileName(full);
                NotifyFast(full, fail, false, "DECODE_FAILED", swAll.Elapsed.TotalMilliseconds);
                return Results.Json(fail);
            }
            var decodeMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var p = StrictPreset().Clone();
            p.StrictPair = true;                       // 严格模式恒开，和 /path/strict 的默认一致
            var result = Inspector.Detect(rgba, w, h, p, false, false, Path.GetFileName(full));
            var detectMs = sw.Elapsed.TotalMilliseconds;

            var timing = result.Timing ?? new TimingDto();
            result.Timing = timing;
            timing.ReadMs = Math.Round(readMs, 2);
            timing.DecodeMs = Math.Round(decodeMs, 2);
            timing.DetectMs = Math.Round(detectMs, 2);
            timing.EncodeMs = 0;
            timing.TotalMs = Math.Round(readMs + decodeMs + detectMs, 2);
            timing.FileBytes = bytes.Length;
            NotifyFast(full, result, true, result.ErrorCode, timing.TotalMs);
            return Results.Json(result);
        }
    }

    /// <summary>极速接口的重试次数（默认 3）：上位机覆盖写同一张图时，读到半个文件会自动重试。</summary>
    private static readonly int FastAttempts =
        Math.Max(1, int.TryParse(Environment.GetEnvironmentVariable("RINGINSPECT_FAST_RETRY"), out var _n) && _n > 0 ? _n : 3);
    private const int FastRetryDelayMs = 30;

    /// <summary>读文件 + 校验这期间文件没被改动（上位机原地覆盖时会变），避免拿到半张图。</summary>
    private static bool TryReadStable(string full, out byte[] bytes, out string? error)
    {
        bytes = Array.Empty<byte>();
        error = null;
        long len;
        DateTime stamp;
        try
        {
            var fi = new FileInfo(full);
            if (!fi.Exists) { error = "文件不存在：" + Path.GetFileName(full); return false; }
            len = fi.Length;
            stamp = fi.LastWriteTimeUtc;
        }
        catch (Exception) { error = "读取文件失败"; return false; }

        try { bytes = ReadFileFast(full); }
        catch (Exception) { error = "读取文件失败"; return false; }

        try
        {
            var fi2 = new FileInfo(full);
            if (fi2.Exists && (fi2.Length != len || fi2.LastWriteTimeUtc != stamp))
            {
                error = "文件正在写入";
                return false;
            }
        }
        catch { /* 校验失败就不校验了 */ }
        return true;
    }

    private static void NotifyFast(string full, DetectResult result, bool ok, string? errorCode, double elapsedMs)
    {
        var cb = OnFastResult;
        if (cb == null) return;
        try
        {
            cb(new FastOutcome
            {
                FullPath = full,
                FileName = Path.GetFileName(full),
                Ok = ok,
                Verdict = result.Verdict,
                ErrorCode = errorCode,
                Result = result,
                ElapsedMs = elapsedMs,
            });
        }
        catch { /* 宿主回调不能影响接口本身 */ }
    }

    /// <summary>
    /// 读整文件：按长度一次分配（未清零）+ 顺序扫描标志，省掉 File.ReadAllBytes 的 35MB 清零。
    /// 共享方式放开读写，上位机原地覆盖同一张图时也能读。
    /// </summary>
    private static byte[] ReadFileFast(string full)
    {
        using var fs = new FileStream(full, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        long lenL = fs.Length;
        if (lenL <= 0 || lenL > MaxImageBytes) throw new IOException("文件大小不合法");
        int len = (int)lenL;
        var buf = GC.AllocateUninitializedArray<byte>(len);
        int read = 0;
        while (read < len)
        {
            int n = fs.Read(buf, read, len - read);
            if (n <= 0) break;
            read += n;
        }
        if (read != len) Array.Resize(ref buf, read);
        return buf;
    }

    private static DetectResult RunDetect(byte[] rgba, int w, int h, DetectParams p,
        bool includeProfile, bool includeOverlay, string? fileName, AppliedParamDto? applied)
    {
        var result = Inspector.Detect(rgba, w, h, p, includeProfile, includeOverlay, fileName);
        result.Applied = applied;
        return result;
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
        if (i.BreakMinWidth.HasValue) yield return ("breakMinWidth", i.BreakMinWidth.Value);
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

    /// <summary>开关关掉时，所有检测接口统一返回 409 + DETECTION_PAUSED。</summary>
    private static IResult? Paused() =>
        DetectionSwitch.Enabled
            ? null
            : Error("DETECTION_PAUSED",
                    "检测已暂停（开关已关闭）。用桌面界面的开关或 POST /api/switch {\"enabled\":true} 恢复。",
                    409);

    /// <summary>联机武装检查：上位机还没发 start（或已发 end）时，极速接口不接单。</summary>
    private static IResult? NotArmed() =>
        IsArmed is { } gate && !gate()
            ? Error("NOT_ARMED", "还没收到上位机的开始指令（start），暂不受理检测请求。", 409)
            : null;

    // ---------- blob 接口辅助 ----------
    private const long MaxImageBytes = 128L * 1024 * 1024;

    /// <summary>调试开关：设 RINGINSPECT_OLD_BODYREAD=1 可退回老写法（MemoryStream + CopyToAsync），A/B 用。</summary>
    private static readonly bool OldBodyRead =
        Environment.GetEnvironmentVariable("RINGINSPECT_OLD_BODYREAD") == "1";

    /// <summary>读取 HTTP 请求体原始字节（blob 接口用）。返回 (字节, 收取耗时 ms)；空 body 返回 (null, ms)。</summary>
    private static async Task<(byte[]? Bytes, double ReadMs)> ReadBodyBytesAsync(HttpRequest http)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var len = http.ContentLength;
        if (len is <= 0 or > MaxImageBytes) return (null, sw.Elapsed.TotalMilliseconds);

        if (OldBodyRead)
        {
            using var msOld = new MemoryStream();
            await http.Body.CopyToAsync(msOld);
            if (msOld.Length == 0 || msOld.Length > MaxImageBytes) return (null, sw.Elapsed.TotalMilliseconds);
            return (msOld.ToArray(), sw.Elapsed.TotalMilliseconds);
        }

        // 有 Content-Length：一次分配到最终大小，直接读进目标数组（整块会被覆盖，用未清零分配）。
        // 老写法用无容量 MemoryStream + CopyToAsync + ToArray()，35MB 图要多拷 ~110MB（扩容复制 + 尾部复制）。
        var buf = GC.AllocateUninitializedArray<byte>((int)len.GetValueOrDefault());
        int read = 0;
        var body = http.Body;
        var abort = http.HttpContext.RequestAborted;
        while (read < buf.Length)
        {
            int n = await body.ReadAsync(buf.AsMemory(read, buf.Length - read), abort);
            if (n <= 0) break;
            read += n;
        }
        if (read != buf.Length) Array.Resize(ref buf, read);   // 客户端提前断开时按实际长度收尾
        if (read == 0) return (null, sw.Elapsed.TotalMilliseconds);
        return (buf, sw.Elapsed.TotalMilliseconds);
    }

    private static bool QueryBool(HttpRequest http, string name, bool fallback)
    {
        var raw = http.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        raw = raw.Trim();
        if (raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw == "0" || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return false;
        return fallback;
    }

    /// <summary>blob 接口的参数覆盖：16 个参数各按同名 query 传数字。</summary>
    private static bool TryApplyQueryParams(HttpRequest http, out DetectParams p, out string? error)
    {
        p = DetectParams.CreateDefault();
        error = null;
        foreach (var spec in ParamRegistry.All)
        {
            var raw = http.Query[spec.Key].ToString();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                error = $"参数 {spec.Key} 不是合法数字";
                return false;
            }
            if (!Validate(spec, v, out error)) return false;
            spec.Set(p, v);
        }
        return true;
    }

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
