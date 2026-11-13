using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TensileNeW.Models;

namespace TensileNeW.Services;

public sealed record VisionCameraSdkSelection(string BrandName, IVisionCameraServiceFactory? Factory, string? Error);

public static class VisionCameraSdkResolver
{
    private const uint LoadWithAlteredSearchPath = 0x00000008;
    private const string MvsBrand = "MVS";
    private const string DahengBrand = "Daheng";
    private static readonly string[] DahengNativeFiles =
    [
        "GxIAPI.dll", "GxIAPICPP.dll", "DxImageProc.dll", "GCBase_MD_VC120_v3_0.dll",
        "GenApi_MD_VC120_v3_0.dll", "MathParser_MD_VC120_v3_0.dll", "XmlParser_MD_VC120_v3_0.dll",
        "Log_MD_VC120_v3_0.dll", "NodeMapData_MD_VC120_v3_0.dll", "log4cpp_MD_VC120_v3_0.dll",
        "msvcr100.dll", "msvcp100.dll", "msvcr120.dll", "msvcp120.dll", "msvcp140.dll",
        "vcruntime140.dll", "vcomp140.dll", "GenCP_MD_VC120_v3_0.dll",
        "GxGVTL.cti", "GxU3VTL.cti", "GxUSBTL.cti", "GxGevGrabberTL.cti", "GxCXPTL.cti"
    ];
    public static VisionCameraSdkSelection Resolve()
    {
        string selected = RAM.SettingModel.VisionCameraBrand;
        if (Debugger.IsAttached) selected = AskDebugCameraBrand(selected);
        string name = selected switch
        {
            DahengBrand => "大恒",
            MvsBrand => "华睿",
            _ => "相机"
        };
        if (selected is not (MvsBrand or DahengBrand))
            return new VisionCameraSdkSelection(name, null, "相机品牌配置无效，请联系管理员。");

#if CAMERA_MVS_ONLY
        if (selected != MvsBrand)
            return new VisionCameraSdkSelection(name, null, $"{name} SDK 缺失，请联系管理员。");
#elif CAMERA_DAHENG_ONLY
        if (selected != DahengBrand)
            return new VisionCameraSdkSelection(name, null, $"{name} SDK 缺失，请联系管理员。");
#endif
        try
        {
            if (selected == MvsBrand)
            {
                IntPtr installedSdk = LoadLibraryEx("MVSDKmd.dll", IntPtr.Zero, 0);
                if (installedSdk == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                FreeLibrary(installedSdk);
            }
            else
            {
                string directory = Path.Combine(AppContext.BaseDirectory, "CameraSdk", DahengBrand, "Native");
                foreach (string nameOfFile in DahengNativeFiles)
                    if (!File.Exists(Path.Combine(directory, nameOfFile)))
                        throw new FileNotFoundException(nameOfFile);
                Environment.SetEnvironmentVariable("GENICAM_GENTL64_PATH", directory);
                if (!SetDllDirectory(directory))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                IntPtr nativeSdk = LoadLibraryEx(Path.Combine(directory, "GxIAPICPP.dll"), IntPtr.Zero, LoadWithAlteredSearchPath);
                if (nativeSdk == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                FreeLibrary(nativeSdk);
            }
            IVisionCameraServiceFactory factory = selected switch
            {
#if !CAMERA_DAHENG_ONLY
                MvsBrand => new MvsCameraSdkServiceFactory(),
#endif
#if !CAMERA_MVS_ONLY
                DahengBrand => new DahengCameraSdkServiceFactory(),
#endif
                _ => throw new InvalidOperationException("相机品牌配置无效。")
            };
            return new VisionCameraSdkSelection(name, factory, null);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DllNotFoundException or
               BadImageFormatException or System.ComponentModel.Win32Exception or TypeLoadException)
        {
            return new VisionCameraSdkSelection(name, null, $"{name} SDK 缺失，请联系管理员。");
        }
    }

    private static string AskDebugCameraBrand(string configuredBrand)
    {
        if (GetConsoleWindow() == IntPtr.Zero && !AllocConsole()) return configuredBrand;
        Console.InputEncoding = new System.Text.UTF8Encoding(false);
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        while (true)
        {
            Console.Write("调试相机品牌：1. 华睿 2. 大恒 (1/2): ");
            string? choice = Console.ReadLine()?.Trim();
            if (choice is null) return configuredBrand;
            if (choice == "1") return MvsBrand;
            if (choice == "2") return DahengBrand;
            Console.WriteLine("请输入 1 或 2。");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
