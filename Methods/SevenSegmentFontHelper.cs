using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using NLog;

namespace TensileNeW;

public static class SevenSegmentFontHelper
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const string FontFileName = "Seven Segment.ttf";
    private const string AssetsFolderName = "Assets";
    private const string FontFamilyName = "Seven Segment";
    private const string FontRegistryName = "Seven Segment (TrueType)";
    private const int HwndBroadcast = 0xffff;
    private const int WmFontchange = 0x001d;
    private const int FrPrivate = 0x10;

    public static FontFamily DefaultFontFamily => SystemFonts.MessageFontFamily;

    public static FontFamily GetFontFamilyOrDefault()
    {
        try
        {
            if (IsFontFamilyAvailable(FontFamilyName))
            {
                return new FontFamily(FontFamilyName);
            }

            string? baseDirectory = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = AppContext.BaseDirectory;
            }

            string fontPath = Path.Combine(baseDirectory, AssetsFolderName, FontFileName);
            if (!File.Exists(fontPath))
            {
                return DefaultFontFamily;
            }

            TryInstallPerUser(fontPath);
            if (IsFontFamilyAvailable(FontFamilyName))
            {
                return new FontFamily(FontFamilyName);
            }

            AddFontResourceEx(fontPath, FrPrivate, IntPtr.Zero);
            return new FontFamily(new Uri(baseDirectory + Path.DirectorySeparatorChar), $"./#{FontFamilyName}");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "七段数码字体加载失败，使用默认字体。");
            return DefaultFontFamily;
        }
    }

    private static bool IsFontFamilyAvailable(string familyName)
    {
        return Fonts.SystemFontFamilies.Any(font =>
            string.Equals(font.Source, familyName, StringComparison.OrdinalIgnoreCase));
    }

    private static void TryInstallPerUser(string sourceFontPath)
    {
        try
        {
            string fontsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "Windows",
                "Fonts");
            Directory.CreateDirectory(fontsDirectory);

            string targetFontPath = Path.Combine(fontsDirectory, FontFileName);
            if (!File.Exists(targetFontPath))
            {
                File.Copy(sourceFontPath, targetFontPath, overwrite: false);
            }

            using RegistryKey fontsKey = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows NT\CurrentVersion\Fonts");
            fontsKey.SetValue(FontRegistryName, targetFontPath, RegistryValueKind.String);
            AddFontResourceEx(targetFontPath, 0, IntPtr.Zero);
            SendMessageTimeout((IntPtr)HwndBroadcast, WmFontchange, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "七段数码字体注册失败。");
            AddFontResourceEx(sourceFontPath, FrPrivate, IntPtr.Zero);
        }
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string name, int fl, IntPtr res);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        int flags,
        int timeout,
        out IntPtr result);
}
