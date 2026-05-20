using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Builder;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
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

        string assemblyName = GetAssemblyName(projectPath);
        string packageDirectory = Path.Combine(outputRoot, assemblyName);
        string? builderOutputDirectory = Directory.GetParent(outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;

        if (!string.IsNullOrWhiteSpace(builderOutputDirectory))
        {
            CleanStaleRootOutput(builderOutputDirectory, assemblyName);
        }

        if (Directory.Exists(packageDirectory))
        {
            Directory.Delete(packageDirectory, recursive: true);
        }

        Directory.CreateDirectory(packageDirectory);

        Console.WriteLine($"Publishing {projectPath}");
        RunProcess(
            "dotnet",
            $"publish --no-restore --nologo {Quote(projectPath)} -c {Quote(configuration)} -o {Quote(packageDirectory)}");

        MoveDependencyDllsToLib(packageDirectory, assemblyName);
        RewriteDepsJson(packageDirectory, assemblyName);
        WriteStartupScript(packageDirectory, assemblyName);

        Console.WriteLine($"Packaged external project to {packageDirectory}");
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
        DeleteDirectoryIfExists(Path.Combine(builderOutputDirectory, "runtimes"));
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string GetAssemblyName(string projectPath)
    {
        XDocument project = XDocument.Load(projectPath);
        string? assemblyName = project
            .Descendants("AssemblyName")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return string.IsNullOrWhiteSpace(assemblyName)
            ? Path.GetFileNameWithoutExtension(projectPath)
            : assemblyName;
    }

    private static void MoveDependencyDllsToLib(string packageDirectory, string assemblyName)
    {
        string libDirectory = Path.Combine(packageDirectory, "lib");
        Directory.CreateDirectory(libDirectory);

        string mainDllName = assemblyName + ".dll";
        foreach (string dllFile in Directory.EnumerateFiles(packageDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(dllFile);
            if (fileName.Equals(mainDllName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string targetFile = Path.Combine(libDirectory, fileName);
            if (File.Exists(targetFile))
            {
                File.Delete(targetFile);
            }

            File.Move(dllFile, targetFile);
        }
    }

    private static void RewriteDepsJson(string packageDirectory, string assemblyName)
    {
        string depsPath = Path.Combine(packageDirectory, assemblyName + ".deps.json");
        if (!File.Exists(depsPath))
        {
            return;
        }

        JsonNode? root = JsonNode.Parse(File.ReadAllText(depsPath));
        JsonObject? targets = root?["targets"] as JsonObject;
        if (targets is null)
        {
            return;
        }

        string mainDllName = assemblyName + ".dll";
        foreach (JsonObject target in targets.Select(item => item.Value).OfType<JsonObject>())
        {
            foreach (JsonObject library in target.Select(item => item.Value).OfType<JsonObject>())
            {
                if (library["runtime"] is JsonObject runtime)
                {
                    RewriteRuntimeEntries(runtime, mainDllName);
                }
            }
        }

        File.WriteAllText(
            depsPath,
            root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RewriteRuntimeEntries(JsonObject runtime, string mainDllName)
    {
        List<KeyValuePair<string, JsonNode?>> entries = runtime.ToList();
        runtime.Clear();

        foreach ((string key, JsonNode? value) in entries)
        {
            string fileName = Path.GetFileName(key);
            string targetKey = fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !fileName.Equals(mainDllName, StringComparison.OrdinalIgnoreCase)
                    ? "lib/" + fileName
                    : key;

            runtime[targetKey] = value;
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
}
