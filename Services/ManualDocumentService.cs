using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TensileNeW.Models;

namespace TensileNeW.Services;

public sealed record ManualDocumentConvertResult(bool Success, string? XpsPath, string? Message)
{
    public static ManualDocumentConvertResult Ok(string xpsPath) => new(true, xpsPath, null);

    public static ManualDocumentConvertResult Fail(string message) => new(false, null, message);
}

public sealed record ManualDocumentStartupResult(bool HasMissingOffice, string? MissingOfficeMessage, bool GeneratedCache);

public static class ManualDocumentService
{
    public const string MissingOfficeMessage = "未安装office或者wps，试验说明文档无法使用。";

    private const string ManualsDirectoryName = "manuals";
    private const string CacheDirectoryName = "manual-cache";
    private const string CacheManifestName = "manual-cache.json";

    private static readonly string[] WebExtensions = [".htm", ".html", ".pdf", ".txt"];
    private static readonly string[] WordExtensions = [".doc", ".docx"];
    private static readonly string[] PowerPointExtensions = [".ppt", ".pptx"];
    private static readonly string[] SupportedManualExtensions =
    [
        ".htm",
        ".html",
        ".pdf",
        ".txt",
        ".doc",
        ".docx",
        ".ppt",
        ".pptx"
    ];

    private static readonly OfficeProvider[] WordProviders =
    [
        new("Microsoft Word", "Word.Application"),
        new("WPS 文字", "KWPS.Application"),
        new("WPS 文字", "WPS.Application")
    ];

    private static readonly OfficeProvider[] PowerPointProviders =
    [
        new("Microsoft PowerPoint", "PowerPoint.Application"),
        new("WPS 演示", "KWPP.Application"),
        new("WPS 演示", "WPP.Application")
    ];

    public static bool HasAnyOfficeProvider =>
        WordProviders.Concat(PowerPointProviders).Any(provider => Type.GetTypeFromProgID(provider.ProgId) is not null);

    public static bool CanOpenInWebView(string path)
    {
        string extension = Path.GetExtension(path);
        return WebExtensions.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase));
    }

    public static bool CanConvertToXps(string path)
    {
        string extension = Path.GetExtension(path);
        return WordExtensions.Concat(PowerPointExtensions)
            .Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<HelpNavigationItem> LoadManualNavigation()
    {
        return EnumerateManualFiles()
            .Select(CreateManualNavigationItem)
            .ToList();
    }

    public static ManualDocumentStartupResult PrepareManualCache()
    {
        IReadOnlyList<string> manualFiles = EnumerateManualFiles();
        bool hasOfficeDocuments = manualFiles.Any(CanConvertToXps);
        if (hasOfficeDocuments && !HasAnyOfficeProvider)
        {
            return new ManualDocumentStartupResult(true, MissingOfficeMessage, false);
        }

        bool generatedCache = false;
        foreach (string manualFile in manualFiles.Where(CanConvertToXps))
        {
            ManualDocumentConvertResult result = ConvertToXpsFile(manualFile);
            if (result.Success)
            {
                generatedCache |= LastConversionGeneratedCache;
            }
        }

        return new ManualDocumentStartupResult(false, null, generatedCache);
    }

    public static bool NeedsToGenerateCache()
    {
        foreach (string manualFile in EnumerateManualFiles().Where(CanConvertToXps))
        {
            if (!TryGetCachedXps(manualFile).Success)
            {
                return true;
            }
        }

        return false;
    }

    [ThreadStatic]
    private static bool _lastConversionGeneratedCache;

    public static bool LastConversionGeneratedCache => _lastConversionGeneratedCache;

    public static ManualDocumentConvertResult ConvertToXpsFile(string sourcePath)
    {
        try
        {
            _lastConversionGeneratedCache = false;
            if (!File.Exists(sourcePath))
            {
                return ManualDocumentConvertResult.Fail("说明书文件不存在。");
            }

            CacheManifest manifest = LoadManifest();
            FileSignature signature = FileSignature.Create(sourcePath);
            CacheEntry? entry = manifest.Entries.FirstOrDefault(item =>
                string.Equals(item.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)
                && item.Signature == signature.Signature
                && item.LastWriteTimeUtcTicks == signature.LastWriteTimeUtcTicks
                && item.Length == signature.Length);

            if (entry is not null)
            {
                string cachedPath = Path.Combine(GetCacheDirectory(), entry.CacheFileName);
                if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length > 0)
                {
                    return ManualDocumentConvertResult.Ok(cachedPath);
                }
            }

            if (!HasAnyOfficeProvider)
            {
                return ManualDocumentConvertResult.Fail(MissingOfficeMessage);
            }

            string cacheFileName = $"{Path.GetFileNameWithoutExtension(sourcePath)}_{signature.Signature[..16]}.xps";
            string xpsPath = Path.Combine(GetCacheDirectory(), cacheFileName);
            string extension = Path.GetExtension(sourcePath);

            ConversionResult conversionResult;
            if (WordExtensions.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase)))
            {
                conversionResult = RunInStaThread(() => ConvertWordToXps(sourcePath, xpsPath));
            }
            else if (PowerPointExtensions.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase)))
            {
                conversionResult = RunInStaThread(() => ConvertPowerPointToXps(sourcePath, xpsPath));
            }
            else
            {
                return ManualDocumentConvertResult.Fail($"不支持的说明书格式：{extension}");
            }

            if (!conversionResult.Success)
            {
                return ManualDocumentConvertResult.Fail(conversionResult.Message);
            }

            if (!File.Exists(xpsPath) || new FileInfo(xpsPath).Length == 0)
            {
                return ManualDocumentConvertResult.Fail("说明书转换 XPS 失败，未生成有效文件。");
            }

            manifest.Entries.RemoveAll(item => string.Equals(item.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
            manifest.Entries.Add(new CacheEntry
            {
                SourcePath = sourcePath,
                CacheFileName = cacheFileName,
                Signature = signature.Signature,
                Length = signature.Length,
                LastWriteTimeUtcTicks = signature.LastWriteTimeUtcTicks
            });
            SaveManifest(manifest);
            _lastConversionGeneratedCache = true;

            return ManualDocumentConvertResult.Ok(xpsPath);
        }
        catch (Exception ex)
        {
            return ManualDocumentConvertResult.Fail($"打开说明书失败：{ex.Message}");
        }
    }

    private static HelpNavigationItem CreateManualNavigationItem(string path)
    {
        HelpNavigationItem item = new()
        {
            Title = Path.GetFileNameWithoutExtension(path),
            FilePath = path,
            IsManualFile = true,
            IsRoot = true
        };

        if (CanConvertToXps(path))
        {
            ManualDocumentConvertResult cached = TryGetCachedXps(path);
            if (cached.Success)
            {
                item.CachedPath = cached.XpsPath;
            }
            else if (!HasAnyOfficeProvider)
            {
                item.IsUnavailable = true;
                item.UnavailableMessage = MissingOfficeMessage;
            }
        }

        return item;
    }

    private static ManualDocumentConvertResult TryGetCachedXps(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return ManualDocumentConvertResult.Fail("说明书文件不存在。");
        }

        CacheManifest manifest = LoadManifest();
        FileSignature signature = FileSignature.Create(sourcePath);
        CacheEntry? entry = manifest.Entries.FirstOrDefault(item =>
            string.Equals(item.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)
            && item.Signature == signature.Signature
            && item.LastWriteTimeUtcTicks == signature.LastWriteTimeUtcTicks
            && item.Length == signature.Length);
        if (entry is null)
        {
            return ManualDocumentConvertResult.Fail("说明书缓存不存在。");
        }

        string cachedPath = Path.Combine(GetCacheDirectory(), entry.CacheFileName);
        return File.Exists(cachedPath) && new FileInfo(cachedPath).Length > 0
            ? ManualDocumentConvertResult.Ok(cachedPath)
            : ManualDocumentConvertResult.Fail("说明书缓存不存在。");
    }

    private static IReadOnlyList<string> EnumerateManualFiles()
    {
        string manualsDirectory = GetManualsDirectory();
        if (!Directory.Exists(manualsDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(manualsDirectory, "*", SearchOption.AllDirectories)
            .Where(IsSupportedManualFile)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsSupportedManualFile(string path)
    {
        string extension = Path.GetExtension(path);
        return SupportedManualExtensions.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase));
    }

    private static ConversionResult RunInStaThread(Func<ConversionResult> action)
    {
        ConversionResult? result = null;
        Exception? exception = null;
        Thread thread = new(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            return ConversionResult.Fail(exception.Message);
        }

        return result ?? ConversionResult.Fail("说明书转换失败。");
       
    }
    
    private static ConversionResult ConvertWordToXps(string sourcePath, string xpsPath)
    {
        return TryProviders(
            WordProviders,
            "未安装 Microsoft Word 或 WPS 文字，无法预览 Word 说明书。",
            provider => ConvertWordToXps(provider, sourcePath, xpsPath));
    }

    private static ConversionResult ConvertPowerPointToXps(string sourcePath, string xpsPath)
    {
        return TryProviders(
            PowerPointProviders,
            "未安装 Microsoft PowerPoint 或 WPS 演示，无法预览 PPT 说明书。",
            provider => ConvertPowerPointToXps(provider, sourcePath, xpsPath));
    }

    private static ConversionResult TryProviders(
        IReadOnlyList<OfficeProvider> providers,
        string notInstalledMessage,
        Func<OfficeProvider, ConversionResult> convert)
    {
        List<string> failures = [];
        bool hasInstalledProvider = false;

        foreach (OfficeProvider provider in providers)
        {
            if (Type.GetTypeFromProgID(provider.ProgId) is null)
            {
                continue;
            }

            hasInstalledProvider = true;
            ConversionResult result = convert(provider);
            if (result.Success)
            {
                return result;
            }

            failures.Add($"{provider.Name}：{result.Message}");
        }

        return !hasInstalledProvider
            ? ConversionResult.Fail(notInstalledMessage)
            : ConversionResult.Fail("说明书转换失败。" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static ConversionResult ConvertWordToXps(OfficeProvider provider, string sourcePath, string xpsPath)
    {
        dynamic? app = null;
        dynamic? document = null;
        try
        {
            Type appType = Type.GetTypeFromProgID(provider.ProgId)!;
            app = Activator.CreateInstance(appType);
            if (app is null)
            {
                return ConversionResult.Fail($"无法启动 {provider.Name}。");
            }

            app.Visible = false;
            document = app.Documents.Open(
                FileName: sourcePath,
                ConfirmConversions: false,
                ReadOnly: true,
                AddToRecentFiles: false,
                Visible: false);

            ConversionResult exportResult = TryExportWordAsXps(document, xpsPath);
            return exportResult.Success ? ConversionResult.Ok() : exportResult;
        }
        catch (Exception ex)
        {
            return ConversionResult.Fail(ex.Message);
        }
        finally
        {
            TryClose(document);
            TryQuit(app);
        }
    }

    private static ConversionResult TryExportWordAsXps(dynamic document, string xpsPath)
    {
        DeleteFileIfExists(xpsPath);

        try
        {
            document.ExportAsFixedFormat(
                xpsPath,
                1,
                false,
                0,
                0,
                0,
                0,
                0,
                true,
                true,
                1,
                true,
                true,
                false,
                Type.Missing);
            return ConversionResult.Ok();
        }
        catch
        {
        }

        try
        {
            document.SaveAs2(FileName: xpsPath, FileFormat: 18);
            return ConversionResult.Ok();
        }
        catch
        {
        }

        try
        {
            document.SaveAs(FileName: xpsPath, FileFormat: 18);
            return ConversionResult.Ok();
        }
        catch (Exception ex)
        {
            return ConversionResult.Fail($"无法将 Word 文档转换为 XPS：{ex.Message}");
        }
    }

    private static ConversionResult ConvertPowerPointToXps(OfficeProvider provider, string sourcePath, string xpsPath)
    {
        dynamic? app = null;
        dynamic? presentation = null;
        try
        {
            Type appType = Type.GetTypeFromProgID(provider.ProgId)!;
            app = Activator.CreateInstance(appType);
            if (app is null)
            {
                return ConversionResult.Fail($"无法启动 {provider.Name}。");
            }

            presentation = app.Presentations.Open(
                FileName: sourcePath,
                ReadOnly: true,
                Untitled: false,
                WithWindow: false);
            DeleteFileIfExists(xpsPath);
            presentation.SaveAs(xpsPath, 33);
            return ConversionResult.Ok();
        }
        catch (Exception ex)
        {
            return ConversionResult.Fail(ex.Message);
        }
        finally
        {
            TryClose(presentation);
            TryQuit(app);
        }
    }

    private static string GetBaseDirectory()
    {
        string? exePath = Environment.ProcessPath;
        string? baseDirectory = Path.GetDirectoryName(exePath);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        return string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
    }

    private static string GetManualsDirectory() => Path.Combine(GetBaseDirectory(), ManualsDirectoryName);

    private static string GetCacheDirectory()
    {
        string cacheDirectory = Path.Combine(GetBaseDirectory(), CacheDirectoryName);
        Directory.CreateDirectory(cacheDirectory);
        return cacheDirectory;
    }

    private static string GetManifestPath() => Path.Combine(GetCacheDirectory(), CacheManifestName);

    private static CacheManifest LoadManifest()
    {
        string path = GetManifestPath();
        if (!File.Exists(path))
        {
            return new CacheManifest();
        }

        try
        {
            return JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(path)) ?? new CacheManifest();
        }
        catch
        {
            return new CacheManifest();
        }
    }

    private static void SaveManifest(CacheManifest manifest)
    {
        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(GetManifestPath(), json);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    private static void TryClose(dynamic? document)
    {
        if (document is null)
        {
            return;
        }

        try
        {
            document.Close(false);
        }
        catch
        {
            try
            {
                document.Close();
            }
            catch
            {
            }
        }
    }

    private static void TryQuit(dynamic? app)
    {
        if (app is null)
        {
            return;
        }

        try
        {
            app.Quit(false);
        }
        catch
        {
            try
            {
                app.Quit();
            }
            catch
            {
            }
        }
    }

    private sealed record OfficeProvider(string Name, string ProgId);

    private sealed record ConversionResult(bool Success, string Message)
    {
        public static ConversionResult Ok() => new(true, string.Empty);

        public static ConversionResult Fail(string message) => new(false, message);
    }

    private sealed class CacheManifest
    {
        public List<CacheEntry> Entries { get; set; } = [];
    }

    private sealed class CacheEntry
    {
        public string SourcePath { get; set; } = string.Empty;
        public string CacheFileName { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public long Length { get; set; }
        public long LastWriteTimeUtcTicks { get; set; }
    }

    private sealed record FileSignature(string Signature, long Length, long LastWriteTimeUtcTicks)
    {
        public static FileSignature Create(string path)
        {
            FileInfo info = new(path);
            using FileStream stream = File.OpenRead(path);
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            return new FileSignature(hash, info.Length, info.LastWriteTimeUtc.Ticks);
        }
    }
}
