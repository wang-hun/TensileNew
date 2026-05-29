using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TensileNeW;

public static class NativeTitleBarHelper
{
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    public static void ApplyTheme(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetDwmWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ColorToBgr(ThemeManager.CurrentScheme.CaptionColor));
        SetDwmWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ColorToBgr(ThemeManager.CurrentScheme.CaptionTextColor));
    }

    private static void SetDwmWindowAttribute(IntPtr hwnd, int attribute, uint color)
    {
        _ = DwmSetWindowAttribute(hwnd, attribute, ref color, Marshal.SizeOf<uint>());
    }

    private static uint ColorToBgr(System.Windows.Media.Color color)
    {
        return ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref uint pvAttribute,
        int cbAttribute);
}
