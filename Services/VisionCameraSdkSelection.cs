using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TensileNeW.Models;

namespace TensileNeW.Services;

public sealed record VisionCameraSdkSelection(string BrandName, IVisionCameraServiceFactory? Factory, string? Error);

public static class VisionCameraSdkResolver
{
    private const string MvsBrand = "MVS";
    private const string DahengBrand = "Daheng";
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
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
