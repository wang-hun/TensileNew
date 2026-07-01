using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace EcsInstaller;

internal sealed record InstallerOptions(string InstallPath, bool CreateDesktopShortcut);

internal static class InstallerService
{
    private const string PayloadResourceName = "EcsInstaller.Payload.payload.zip";
    private const string AppExeName = "ECS.exe";
    private const string FallbackPackageDirectoryName = "ECS";

    public static string Install(InstallerOptions options, Action<string> reportProgress)
    {
        string installPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.InstallPath));
        string? parentDirectory = Directory.GetParent(installPath)?.FullName;
        string targetDirectoryName = Path.GetFileName(installPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parentDirectory) || string.IsNullOrWhiteSpace(targetDirectoryName))
        {
            throw new InvalidOperationException("部署路径无效。");
        }

        Directory.CreateDirectory(parentDirectory);
        CleanupStaleWorkingDirectories(parentDirectory, targetDirectoryName);

        string stagingRoot = Path.Combine(parentDirectory, $"{targetDirectoryName}.__installing_{Guid.NewGuid():N}");
        string backupRoot = Path.Combine(parentDirectory, $"{targetDirectoryName}.__backup_{Guid.NewGuid():N}");
        bool backupCreated = false;

        try
        {
            reportProgress("正在释放文件");
            Directory.CreateDirectory(stagingRoot);
            ExtractPayload(stagingRoot);

            string packageRoot = NormalizePackageRoot(stagingRoot);
            string exePath = Path.Combine(packageRoot, AppExeName);
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("未找到 ECS.exe。", exePath);
            }

            reportProgress("正在生成说明文件缓存");
            SilentManualCacheBuilder.Prepare(packageRoot);
            RewriteManualCacheManifestPaths(packageRoot, installPath);

            reportProgress("正在替换部署目录");
            if (Directory.Exists(installPath))
            {
                Directory.Move(installPath, backupRoot);
                backupCreated = true;
            }

            Directory.Move(packageRoot, installPath);
            DeleteDirectoryIfExists(stagingRoot);

            if (backupCreated)
            {
                DeleteDirectoryIfExists(backupRoot);
                backupCreated = false;
            }

            exePath = Path.Combine(installPath, AppExeName);
            if (options.CreateDesktopShortcut)
            {
                reportProgress("正在创建桌面快捷方式");
                CreateShortcut(exePath);
            }

            return exePath;
        }
        catch
        {
            if (!Directory.Exists(installPath) && backupCreated && Directory.Exists(backupRoot))
            {
                Directory.Move(backupRoot, installPath);
                backupCreated = false;
            }

            throw;
        }
        finally
        {
            DeleteDirectoryIfExists(stagingRoot);
            if (backupCreated)
            {
                DeleteDirectoryIfExists(backupRoot);
            }
        }
    }

    public static string GetDefaultInstallPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            GetPayloadPackageDirectoryName());
    }

    public static string AppendPackageDirectory(string parentDirectory)
    {
        string expandedPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(parentDirectory));
        string packageDirectoryName = GetPayloadPackageDirectoryName();
        return Path.GetFileName(expandedPath).Equals(packageDirectoryName, StringComparison.OrdinalIgnoreCase)
            ? expandedPath
            : Path.Combine(expandedPath, packageDirectoryName);
    }

    private static string GetPayloadPackageDirectoryName()
    {
        try
        {
            using Stream? payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName);
            if (payloadStream is null)
            {
                return FallbackPackageDirectoryName;
            }

            using ZipArchive archive = new(payloadStream, ZipArchiveMode.Read);
            string? packageDirectoryName = archive.Entries
                .Select(entry => entry.FullName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .GroupBy(name => name!, StringComparer.OrdinalIgnoreCase)
                .SingleOrDefault()
                ?.Key;

            if (!string.IsNullOrWhiteSpace(packageDirectoryName))
            {
                return packageDirectoryName;
            }
        }
        catch
        {
        }

        return FallbackPackageDirectoryName;
    }

    private static void ExtractPayload(string installPath)
    {
        using Stream? payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName);
        if (payloadStream is null)
        {
            throw new InvalidOperationException("安装器未内嵌发布包。请使用项目生成的 publish 目录中的 ECS-Installer.exe。");
        }

        using ZipArchive archive = new(payloadStream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(installPath, overwriteFiles: true);
    }

    private static string NormalizePackageRoot(string installPath)
    {
        if (File.Exists(Path.Combine(installPath, AppExeName)))
        {
            return installPath;
        }

        string[] candidates = Directory
            .EnumerateDirectories(installPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => File.Exists(Path.Combine(path, AppExeName)))
            .ToArray();

        return candidates.Length == 1 ? candidates[0] : installPath;
    }

    private static void RewriteManualCacheManifestPaths(string stagingPackageRoot, string finalPackageRoot)
    {
        string manifestPath = Path.Combine(stagingPackageRoot, "manual-cache", "manual-cache.json");
        if (!File.Exists(manifestPath))
        {
            return;
        }

        try
        {
            CacheManifest? manifest = JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(manifestPath));
            if (manifest is null)
            {
                return;
            }

            foreach (CacheEntry entry in manifest.Entries)
            {
                if (!entry.SourcePath.StartsWith(stagingPackageRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(stagingPackageRoot, entry.SourcePath);
                entry.SourcePath = Path.Combine(finalPackageRoot, relativePath);
            }

            string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json);
        }
        catch
        {
        }
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

    private static void CleanupStaleWorkingDirectories(string parentDirectory, string targetDirectoryName)
    {
        IEnumerable<string> staleDirectories = Directory
            .EnumerateDirectories(parentDirectory, $"{targetDirectoryName}.__installing_*", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateDirectories(parentDirectory, $"{targetDirectoryName}.__backup_*", SearchOption.TopDirectoryOnly));

        foreach (string directory in staleDirectories)
        {
            DeleteDirectoryIfExists(directory);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        ResetAttributes(path);
        Directory.Delete(path, recursive: true);
    }

    private static void ResetAttributes(string path)
    {
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (string directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Directory);
        }

        File.SetAttributes(path, FileAttributes.Directory);
    }

    private static void CreateShortcut(string exePath)
    {
        string shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "ECS.lnk");

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shell?.CreateShortcut(shortcutPath);
            if (shortcut is null)
            {
                return;
            }

            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
            shortcut.IconLocation = exePath;
            shortcut.Save();
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}

internal static class SilentManualCacheBuilder
{
    private const string ManualsDirectoryName = "manuals";
    private const string CacheDirectoryName = "manual-cache";
    private const string CacheManifestName = "manual-cache.json";

    private static readonly string[] WordExtensions = [".doc", ".docx"];
    private static readonly string[] PowerPointExtensions = [".ppt", ".pptx"];

    private static readonly OfficeProvider[] WordProviders =
    [
        new("Word.Application"),
        new("KWPS.Application"),
        new("WPS.Application")
    ];

    private static readonly OfficeProvider[] PowerPointProviders =
    [
        new("PowerPoint.Application"),
        new("KWPP.Application"),
        new("WPP.Application")
    ];

    public static void Prepare(string packageRoot)
    {
        try
        {
            string manualsDirectory = Path.Combine(packageRoot, ManualsDirectoryName);
            if (!Directory.Exists(manualsDirectory) || !HasAnyOfficeProvider())
            {
                return;
            }

            foreach (string manualFile in Directory.EnumerateFiles(manualsDirectory, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(manualFile);
                if (WordExtensions.Any(item => item.Equals(extension, StringComparison.OrdinalIgnoreCase)))
                {
                    TryConvert(packageRoot, manualFile, WordProviders, ConvertWordToXps);
                }
                else if (PowerPointExtensions.Any(item => item.Equals(extension, StringComparison.OrdinalIgnoreCase)))
                {
                    TryConvert(packageRoot, manualFile, PowerPointProviders, ConvertPowerPointToXps);
                }
            }
        }
        catch
        {
        }
    }

    private static bool HasAnyOfficeProvider()
    {
        return WordProviders.Concat(PowerPointProviders).Any(provider => Type.GetTypeFromProgID(provider.ProgId) is not null);
    }

    private static void TryConvert(
        string packageRoot,
        string sourcePath,
        IReadOnlyList<OfficeProvider> providers,
        Func<OfficeProvider, string, string, bool> convert)
    {
        try
        {
            CacheManifest manifest = LoadManifest(packageRoot);
            FileSignature signature = FileSignature.Create(sourcePath);
            CacheEntry? entry = manifest.Entries.FirstOrDefault(item =>
                string.Equals(item.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)
                && item.Signature == signature.Signature
                && item.LastWriteTimeUtcTicks == signature.LastWriteTimeUtcTicks
                && item.Length == signature.Length);

            if (entry is not null)
            {
                string cachedPath = Path.Combine(GetCacheDirectory(packageRoot), entry.CacheFileName);
                if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length > 0)
                {
                    return;
                }
            }

            foreach (OfficeProvider provider in providers)
            {
                if (Type.GetTypeFromProgID(provider.ProgId) is null)
                {
                    continue;
                }

                string cacheFileName = $"{Path.GetFileNameWithoutExtension(sourcePath)}_{signature.Signature[..16]}.xps";
                string xpsPath = Path.Combine(GetCacheDirectory(packageRoot), cacheFileName);
                if (!RunInStaThread(() => convert(provider, sourcePath, xpsPath)))
                {
                    continue;
                }

                if (!File.Exists(xpsPath) || new FileInfo(xpsPath).Length == 0)
                {
                    continue;
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
                SaveManifest(packageRoot, manifest);
                return;
            }
        }
        catch
        {
        }
    }

    private static bool RunInStaThread(Func<bool> action)
    {
        bool result = false;
        Thread thread = new(() =>
        {
            try
            {
                result = action();
            }
            catch
            {
                result = false;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static bool ConvertWordToXps(OfficeProvider provider, string sourcePath, string xpsPath)
    {
        dynamic? app = null;
        dynamic? document = null;
        try
        {
            Type appType = Type.GetTypeFromProgID(provider.ProgId)!;
            app = Activator.CreateInstance(appType);
            if (app is null)
            {
                return false;
            }

            app.Visible = false;
            document = app.Documents.Open(
                FileName: sourcePath,
                ConfirmConversions: false,
                ReadOnly: true,
                AddToRecentFiles: false,
                Visible: false);

            DeleteFileIfExists(xpsPath);
            try
            {
                document.ExportAsFixedFormat(xpsPath, 1, false, 0, 0, 0, 0, 0, true, true, 1, true, true, false, Type.Missing);
                return true;
            }
            catch
            {
            }

            try
            {
                document.SaveAs2(FileName: xpsPath, FileFormat: 18);
                return true;
            }
            catch
            {
            }

            document.SaveAs(FileName: xpsPath, FileFormat: 18);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            TryClose(document);
            TryQuit(app);
        }
    }

    private static bool ConvertPowerPointToXps(OfficeProvider provider, string sourcePath, string xpsPath)
    {
        dynamic? app = null;
        dynamic? presentation = null;
        try
        {
            Type appType = Type.GetTypeFromProgID(provider.ProgId)!;
            app = Activator.CreateInstance(appType);
            if (app is null)
            {
                return false;
            }

            presentation = app.Presentations.Open(
                FileName: sourcePath,
                ReadOnly: true,
                Untitled: false,
                WithWindow: false);
            DeleteFileIfExists(xpsPath);
            presentation.SaveAs(xpsPath, 33);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            TryClose(presentation);
            TryQuit(app);
        }
    }

    private static string GetCacheDirectory(string packageRoot)
    {
        string cacheDirectory = Path.Combine(packageRoot, CacheDirectoryName);
        Directory.CreateDirectory(cacheDirectory);
        return cacheDirectory;
    }

    private static string GetManifestPath(string packageRoot) => Path.Combine(GetCacheDirectory(packageRoot), CacheManifestName);

    private static CacheManifest LoadManifest(string packageRoot)
    {
        string path = GetManifestPath(packageRoot);
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

    private static void SaveManifest(string packageRoot, CacheManifest manifest)
    {
        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(GetManifestPath(packageRoot), json);
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

    private sealed record OfficeProvider(string ProgId);

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
