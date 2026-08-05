using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace InstallerBuilder;

internal static class Program
{
    private const string InstallerAssemblyName = "ECS-Installer";

    private static int Main()
    {
        ConfigureConsoleEncoding();

        try
        {
            string repositoryRoot = FindRepositoryRoot();
            PackageMode packageMode = AskPackageMode();
            PackageInstaller(repositoryRoot, packageMode);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void PackageInstaller(string repositoryRoot, PackageMode packageMode)
    {
        string mainProjectPath = Path.Combine(repositoryRoot, "TensileNeW.csproj");
        string builderProjectPath = Path.Combine(repositoryRoot, "builder", "Builder", "Builder.csproj");
        string installerProjectPath = Path.Combine(repositoryRoot, "installer", "Installer", "Installer.csproj");
        EnsureFileExists(mainProjectPath);
        EnsureFileExists(builderProjectPath);
        EnsureFileExists(installerProjectPath);

        string workingRoot = Path.Combine(Path.GetTempPath(), "EcsInstallerBuilder", Guid.NewGuid().ToString("N"));
        string payloadRoot = Path.Combine(workingRoot, "payload-root");
        string payloadZip = Path.Combine(workingRoot, "payload.zip");
        string publishDirectory = Path.Combine(AppContext.BaseDirectory, "publish");
        ProjectVersion projectVersion = ReadProjectVersion(mainProjectPath);

        try
        {
            Directory.CreateDirectory(payloadRoot);
            Console.WriteLine("正在生成安装器内容物...");
            RunDotnet(
                $"run --project {Quote(builderProjectPath)} --no-launch-profile -- pack " +
                $"{Quote(mainProjectPath)} Release {Quote(payloadRoot)} {GetBuilderPackageArguments(packageMode)}");

            ZipFile.CreateFromDirectory(payloadRoot, payloadZip, CompressionLevel.Optimal, includeBaseDirectory: false);

            DeleteDirectoryIfExists(publishDirectory);
            Directory.CreateDirectory(publishDirectory);
            Console.WriteLine("正在发布安装器...");
            RunDotnet(
                $"publish {Quote(installerProjectPath)} -c Release -r win-x64 --self-contained true " +
                "-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true " +
                $"-p:InstallerPayloadZip={Quote(payloadZip)} " +
                $"-p:Version={Quote(projectVersion.Version)} -p:AssemblyVersion={Quote(projectVersion.Version)} " +
                $"-p:FileVersion={Quote(projectVersion.Version)} -p:InformationalVersion={Quote(projectVersion.InformationalVersion)} " +
                $"-p:DebugType=None -p:DebugSymbols=false -o {Quote(publishDirectory)}");

            DeleteFileIfExists(Path.Combine(publishDirectory, InstallerAssemblyName + ".pdb"));
            DeleteFileIfExists(Path.Combine(publishDirectory, InstallerAssemblyName + ".xml"));
            EnsureSingleFileInstaller(publishDirectory);
            Console.WriteLine($"安装器已生成：{publishDirectory}");
        }
        finally
        {
            DeleteDirectoryIfExists(workingRoot);
        }
    }

    private static PackageMode AskPackageMode()
    {
        while (true)
        {
            Console.Write("是否生成试用版安装器？(Y/N): ");
            string? answer = Console.ReadLine();
            if (string.Equals(answer?.Trim(), "Y", StringComparison.OrdinalIgnoreCase))
            {
                return PackageMode.Trial;
            }

            if (string.Equals(answer?.Trim(), "N", StringComparison.OrdinalIgnoreCase))
            {
                return AskNonTrialPackageMode();
            }

            Console.WriteLine("请输入 Y 或 N。");
        }
    }

    private static PackageMode AskNonTrialPackageMode()
    {
        while (true)
        {
            Console.Write("请选择非试用版模式：1. 带完整版权限配置文件 2. 不带完整版权限配置文件 (1/2): ");
            string? answer = Console.ReadLine();
            if (answer?.Trim() is "1")
            {
                return PackageMode.Full;
            }

            if (answer?.Trim() is "2")
            {
                return PackageMode.WithoutFullPermissionConfiguration;
            }

            Console.WriteLine("请输入 1 或 2。");
        }
    }

    private static string GetBuilderPackageArguments(PackageMode packageMode) => packageMode switch
    {
        PackageMode.Trial => "Y",
        PackageMode.Full => "N 1",
        PackageMode.WithoutFullPermissionConfiguration => "N 2",
        _ => throw new ArgumentOutOfRangeException(nameof(packageMode))
    };

    private enum PackageMode
    {
        Trial,
        Full,
        WithoutFullPermissionConfiguration
    }

    private static void EnsureSingleFileInstaller(string publishDirectory)
    {
        string installerPath = Path.Combine(publishDirectory, InstallerAssemblyName + ".exe");
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("Installer publish did not produce the expected executable.", installerPath);
        }

        string[] looseDlls = Directory.EnumerateFiles(publishDirectory, "*.dll", SearchOption.TopDirectoryOnly).ToArray();
        if (looseDlls.Length > 0)
        {
            throw new InvalidOperationException("Installer publish left loose DLL(s): " + string.Join(", ", looseDlls));
        }
    }

    private static ProjectVersion ReadProjectVersion(string projectPath)
    {
        XDocument project = XDocument.Load(projectPath);
        string version = project.Descendants("Version").Select(element => element.Value.Trim()).FirstOrDefault() ?? "1.0.0";
        string informationalVersion = project.Descendants("InformationalVersion").Select(element => element.Value.Trim()).FirstOrDefault() ?? version;
        return new ProjectVersion(version, informationalVersion);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TensileNeW.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 TensileNeW.csproj。");
    }

    private static void RunDotnet(string arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                Console.WriteLine(eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                Console.Error.WriteLine(eventArgs.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet exited with code {process.ExitCode}.");
        }
    }

    private static void ConfigureConsoleEncoding()
    {
        const uint Utf8CodePage = 65001;
        SetConsoleCP(Utf8CodePage);
        SetConsoleOutputCP(Utf8CodePage);

        Encoding utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.InputEncoding = utf8;
        Console.OutputEncoding = utf8;
    }

    private static void EnsureFileExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required project was not found.", path);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCP(uint codePage);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleOutputCP(uint codePage);

    private sealed record ProjectVersion(string Version, string InformationalVersion);
}
