using System.Diagnostics;
using System.Xml.Linq;
using System.Text;
using TensileNeW.Services;

namespace Builder;

internal static class Program
{
    private const string ManualsSourceDirectory = @"E:\ECS说明书";
    private const string ManualsOutputDirectoryName = "manuals";
    private const string DefaultRuntimeIdentifier = "win-x64";

    private static int Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        try
        {
            if (args.Length == 0)
            {
                string defaultProjectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TensileNeW.csproj"));
                string defaultConfiguration = "Release";
                string defaultOutputRoot = Path.Combine(AppContext.BaseDirectory, "publish");

                PackageExternalProject(defaultProjectPath, defaultConfiguration, defaultOutputRoot);
                return 0;
            }

            if (args.Length > 0 && args[0].Equals("pack", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 4)
                {
                    Console.Error.WriteLine("Usage: Builder pack <project-path> <configuration> <output-root>");
                    return 1;
                }

                PackageExternalProject(args[1], args[2], args[3]);
                return 0;
            }

            Console.WriteLine("Builder output is generated during MSBuild.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void PackageExternalProject(string projectPath, string configuration, string outputRoot)
    {
        projectPath = Path.GetFullPath(projectPath);
        outputRoot = Path.GetFullPath(outputRoot);

        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("External project was not found.", projectPath);
        }

        bool isTrialPackage = AskWhetherTrialPackage();
        ProjectMetadata projectMetadata = GetProjectMetadata(projectPath);
        string assemblyName = projectMetadata.AssemblyName;
        string packageDirectory = Path.Combine(outputRoot, GetPackageDirectoryName(projectMetadata));
        string projectName = Path.GetFileNameWithoutExtension(projectPath);
        string? builderOutputDirectory = Directory.GetParent(outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;

        if (!string.IsNullOrWhiteSpace(builderOutputDirectory))
        {
            CleanStaleRootOutput(builderOutputDirectory, assemblyName);
        }

        DeleteDirectoryIfExists(outputRoot);
        Directory.CreateDirectory(outputRoot);

        if (!projectName.Equals(assemblyName, StringComparison.OrdinalIgnoreCase))
        {
            DeleteDirectoryIfExists(Path.Combine(outputRoot, projectName));
        }

        DeleteDirectoryIfExists(packageDirectory);
        Directory.CreateDirectory(packageDirectory);

        Console.WriteLine($"Publishing {projectPath}");
        RunProcess(
            "dotnet",
            $"publish --nologo {Quote(projectPath)} -c {Quote(configuration)} -r {Quote(DefaultRuntimeIdentifier)} --self-contained true " +
            "-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true " +
            $"-p:DebugType=None -p:DebugSymbols=false -o {Quote(packageDirectory)}");

        DeleteNonWindowsRuntimes(packageDirectory);
        DeleteUnneededPublishArtifacts(packageDirectory);
        EnsureSingleFileLayout(packageDirectory, assemblyName);
        CopyManualsDirectory(packageDirectory);
        DeleteUnneededPublishArtifacts(packageDirectory);
        WriteStartupScript(packageDirectory, assemblyName);
        EnsureStartupScriptExists(packageDirectory, assemblyName);
        TrialPackageConfiguration.Write(
            Path.Combine(packageDirectory, TrialPackageConfiguration.FileName),
            isTrialPackage);

        Console.WriteLine($"Packaged external project to {packageDirectory}");
    }

    private static bool AskWhetherTrialPackage()
    {
        while (true)
        {
            Console.Write("是否生成试用版配置文件？(Y/N): ");
            string? answer = Console.ReadLine();
            if (string.Equals(answer?.Trim(), "Y", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(answer?.Trim(), "N", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Console.WriteLine("请输入 Y 或 N。");
        }
    }

    private static void EnsureSingleFileLayout(string packageDirectory, string assemblyName)
    {
        string exePath = Path.Combine(packageDirectory, assemblyName + ".exe");
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("Single-file publish did not produce the expected executable.", exePath);
        }

        // A correct single-file self-contained publish embeds the managed assemblies, the
        // .NET runtime and native libraries into the EXE, so no loose *.dll must remain at the
        // package root. If any survive, the publish silently fell back to a multi-file layout
        // and the package is no longer the intended single-file green build.
        List<string> strayDlls = Directory
            .EnumerateFiles(packageDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();

        if (strayDlls.Count > 0)
        {
            throw new InvalidOperationException(
                "Single-file publish left loose DLL(s) at the package root: " + string.Join(", ", strayDlls));
        }
    }

    private static void CopyManualsDirectory(string packageDirectory)
    {
        string sourceDirectory = Path.GetFullPath(ManualsSourceDirectory);
        string targetDirectory = Path.Combine(packageDirectory, ManualsOutputDirectoryName);
        EnsureTargetIsNotSource(sourceDirectory, targetDirectory);

        DeleteDirectoryIfExists(targetDirectory);
        if (!Directory.Exists(sourceDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
            return;
        }

        CopyDirectory(sourceDirectory, targetDirectory);
    }

    private static void EnsureTargetIsNotSource(string sourceDirectory, string targetDirectory)
    {
        string source = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string target = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Manuals target directory must not be the source directory.");
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file);
            string targetFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static void CleanStaleRootOutput(string builderOutputDirectory, string assemblyName)
    {
        foreach (string staleFile in Directory.EnumerateFiles(builderOutputDirectory, assemblyName + ".*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(staleFile);
        }

        string staleNLogConfig = Path.Combine(builderOutputDirectory, "NLog.config");
        if (File.Exists(staleNLogConfig))
        {
            File.Delete(staleNLogConfig);
        }

        DeleteDirectoryIfExists(Path.Combine(builderOutputDirectory, "lib"));
        DeleteDirectoryIfExists(Path.Combine(builderOutputDirectory, "Systemlib"));
        DeleteDirectoryIfExists(Path.Combine(builderOutputDirectory, "runtimes"));
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            ResetAttributes(path);
            Directory.Delete(path, recursive: true);
        }
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

    private static ProjectMetadata GetProjectMetadata(string projectPath)
    {
        XDocument project = XDocument.Load(projectPath);
        string? assemblyName = project
            .Descendants("AssemblyName")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        string? version = project
            .Descendants("InformationalVersion")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new ProjectMetadata(
            string.IsNullOrWhiteSpace(assemblyName) ? Path.GetFileNameWithoutExtension(projectPath) : assemblyName,
            version);
    }

    private static string GetPackageDirectoryName(ProjectMetadata projectMetadata)
    {
        return string.IsNullOrWhiteSpace(projectMetadata.InformationalVersion)
            ? projectMetadata.AssemblyName
            : $"{projectMetadata.AssemblyName} {projectMetadata.InformationalVersion}";
    }

    private static void DeleteUnneededPublishArtifacts(string packageDirectory)
    {
        foreach (string pattern in new[] { "*.pdb", "*.xml" })
        {
            foreach (string file in Directory.EnumerateFiles(packageDirectory, pattern, SearchOption.TopDirectoryOnly))
            {
                File.Delete(file);
            }
        }

        string createdumpPath = Path.Combine(packageDirectory, "createdump.exe");
        if (File.Exists(createdumpPath))
        {
            File.Delete(createdumpPath);
        }

        foreach (string cultureDirectory in GetUnneededSatelliteResourceDirectories(packageDirectory))
        {
            DeleteDirectoryIfExists(cultureDirectory);
        }
    }

    private static IEnumerable<string> GetUnneededSatelliteResourceDirectories(string packageDirectory)
    {
        HashSet<string> cultureNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant"
        };

        return Directory
            .EnumerateDirectories(packageDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(directory => cultureNames.Contains(Path.GetFileName(directory)));
    }

    private static void DeleteNonWindowsRuntimes(string packageDirectory)
    {
        string runtimesDirectory = Path.Combine(packageDirectory, "runtimes");
        if (!Directory.Exists(runtimesDirectory))
        {
            return;
        }

        foreach (string runtimeDirectory in Directory.EnumerateDirectories(runtimesDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            string runtimeName = Path.GetFileName(runtimeDirectory);
            if (runtimeName.StartsWith("win", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DeleteDirectoryIfExists(runtimeDirectory);
        }
    }

    private static void WriteStartupScript(string packageDirectory, string assemblyName)
    {
        string scriptPath = Path.Combine(packageDirectory, $"start-{assemblyName}.cmd");
        string exeName = assemblyName + ".exe";
        string logName = assemblyName + "-startup.log";

        string script = $"""
@echo off
setlocal
cd /d "%~dp0"
set "LOG=%~dp0{logName}"
echo [%date% %time%] Starting {exeName} > "%LOG%"
"%~dp0{exeName}" >> "%LOG%" 2>&1
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" (
    echo {exeName} exited with code %EXITCODE%. See "%LOG%".
    type "%LOG%"
    pause
)
exit /b %EXITCODE%
""";

        File.WriteAllText(scriptPath, script);
    }

    private static void EnsureStartupScriptExists(string packageDirectory, string assemblyName)
    {
        string scriptPath = Path.Combine(packageDirectory, $"start-{assemblyName}.cmd");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Startup diagnostic script was not generated.", scriptPath);
        }
    }

    private static void RunProcess(string fileName, string arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Console.WriteLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Console.Error.WriteLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}.");
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private sealed record ProjectMetadata(string AssemblyName, string? InformationalVersion);
}
