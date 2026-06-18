using System.Collections.Generic;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using MahApps.Metro.IconPacks;
using TensileNeW.Models;
using MediaColors = System.Windows.Media.Colors;

namespace TensileNeW;

public static class ThemeManager
{
    public const string DefaultSchemeName = "警戒";
    private const int BlackGoldBaseRgb = 0xB23526;
    private const int PatchouliBaseRgb = 0x592489;

    public static Color BlackGoldBaseColor { get; } = ColorFromRgb(BlackGoldBaseRgb);
    private static Color PatchouliBaseColor { get; } = ColorFromRgb(PatchouliBaseRgb);

    public static IReadOnlyList<ColorScheme> Schemes { get; } =
    [
        new()
        {
            Name = "警戒",
            CaptionColor = BlackGoldBaseColor,
            CaptionTextColor = ColorFromRgb(0xD4D5CF),
            StatusActiveIconKind = PackIconBootstrapIconsKind.PatchCheckFill,
            Colors = new Dictionary<string, Color>
            {
                ["AppHeaderBackgroundBrush"] = BlackGoldBaseColor,
                ["AppHeaderBorderBrush"] = ColorFromRgb(0xD8DBE3),
                ["AppNavBackgroundBrush"] = BlackGoldBaseColor,
                ["AppNavForegroundBrush"] = MediaColors.White,
                ["AppNavSelectedForegroundBrush"] = ColorFromRgb(0xFFDD00),
                ["AppSidebarBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppSurfaceBackgroundBrush"] = ColorFromRgb(0xE1E2DC),
                ["AppSidebarLabelForegroundBrush"] = ColorFromRgb(0x005BC2),
                ["AppMetricNameBackgroundBrush"] = ColorFromRgb(0xE1E2DC),
                ["AppMetricNameForegroundBrush"] = BlackGoldBaseColor,
                ["AppMetricValueForegroundBrush"] = ColorFromRgb(0x005BC2),
                ["AppHomeCenterBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppLayoutBorderBrush"] = ColorFromRgb(0xFFDD00),
                ["AppReconnectBackgroundBrush"] = ColorFromRgb(0x005BC2),
                ["AppReconnectForegroundBrush"] = MediaColors.White,
                ["AppReconnectHoverForegroundBrush"] = ColorFromRgb(0xFFDD00),
                ["AppReconnectPressedForegroundBrush"] = MediaColors.Black,
                ["AppReconnectBorderBrush"] = ColorFromRgb(0x005BC2),
                ["AppConnectionLabelBrush"] = MediaColors.White,
                ["AppConnectionBadgeBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppStartupWaitBackgroundBrush"] = BlackGoldBaseColor,
                ["AppStartupWaitForegroundBrush"] = MediaColors.White,
                ["AppStartupWaitBorderBrush"] = ColorFromRgb(0xFFDD00),
                ["AppConnectionErrorBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppConnectionErrorForegroundBrush"] = ColorFromRgb(0xD03050)
            }
        },
        new()
        {
            Name = "帕琪",
            CaptionColor = PatchouliBaseColor,
            CaptionTextColor = ColorFromRgb(0xD4D5CF),
            StatusActiveIconKind = PackIconBootstrapIconsKind.PatchCheckFill,
            Colors = new Dictionary<string, Color>
            {
                ["AppHeaderBackgroundBrush"] = PatchouliBaseColor,
                ["AppHeaderBorderBrush"] = ColorFromRgb(0xD8DBE3),
                ["AppNavBackgroundBrush"] = PatchouliBaseColor,
                ["AppNavForegroundBrush"] = MediaColors.White,
                ["AppNavSelectedForegroundBrush"] = ColorFromRgb(0xFFDD00),
                ["AppSidebarBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppSurfaceBackgroundBrush"] = ColorFromRgb(0xE1E2DC),
                ["AppSidebarLabelForegroundBrush"] = ColorFromRgb(0x005BC2),
                ["AppMetricNameBackgroundBrush"] = ColorFromRgb(0xE1E2DC),
                ["AppMetricNameForegroundBrush"] = PatchouliBaseColor,
                ["AppMetricValueForegroundBrush"] = ColorFromRgb(0x005BC2),
                ["AppHomeCenterBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppLayoutBorderBrush"] = ColorFromRgb(0xFFDD00),
                ["AppReconnectBackgroundBrush"] = ColorFromRgb(0x005BC2),
                ["AppReconnectForegroundBrush"] = MediaColors.White,
                ["AppReconnectHoverForegroundBrush"] = ColorFromRgb(0xFFDD00),
                ["AppReconnectPressedForegroundBrush"] = MediaColors.Black,
                ["AppReconnectBorderBrush"] = ColorFromRgb(0x005BC2),
                ["AppConnectionLabelBrush"] = MediaColors.White,
                ["AppConnectionBadgeBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppStartupWaitBackgroundBrush"] = PatchouliBaseColor,
                ["AppStartupWaitForegroundBrush"] = MediaColors.White,
                ["AppStartupWaitBorderBrush"] = ColorFromRgb(0xFFDD00),
                ["AppConnectionErrorBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppConnectionErrorForegroundBrush"] = ColorFromRgb(0xD03050)
            }
        },
        new()
        {
            Name = "论纯白",
            CaptionColor = ColorFromRgb(0xFFFFFF),
            CaptionTextColor = ColorFromRgb(0x000000),
            StatusActiveIconKind = PackIconBootstrapIconsKind.PatchCheck,
            Colors = new Dictionary<string, Color>
            {
                ["AppHeaderBackgroundBrush"] = ColorFromRgb(0xF5F6F8),
                ["AppHeaderBorderBrush"] = ColorFromRgb(0xD8DBE3),
                ["AppNavBackgroundBrush"] = ColorFromRgb(0xF5F6F8),
                ["AppNavForegroundBrush"] = ColorFromRgb(0x606266),
                ["AppNavSelectedForegroundBrush"] = ColorFromRgb(0x1677FF),
                ["AppSidebarBackgroundBrush"] = MediaColors.White,
                ["AppSurfaceBackgroundBrush"] = MediaColors.White,
                ["AppSidebarLabelForegroundBrush"] = ColorFromRgb(0x404040),
                ["AppMetricNameBackgroundBrush"] = ColorFromRgb(0xF2F2F2),
                ["AppMetricNameForegroundBrush"] = ColorFromRgb(0x696969),
                ["AppMetricValueForegroundBrush"] = MediaColors.Black,
                ["AppHomeCenterBackgroundBrush"] = MediaColors.White,
                ["AppLayoutBorderBrush"] = ColorFromRgb(0xC0C4CC),
                ["AppReconnectBackgroundBrush"] = ColorFromRgb(0x1677FF),
                ["AppReconnectForegroundBrush"] = MediaColors.White,
                ["AppReconnectHoverForegroundBrush"] = MediaColors.White,
                ["AppReconnectPressedForegroundBrush"] = MediaColors.Black,
                ["AppReconnectBorderBrush"] = ColorFromRgb(0x1677FF),
                ["AppConnectionLabelBrush"] = MediaColors.Black,
                ["AppConnectionBadgeBackgroundBrush"] = MediaColors.White,
                ["AppStartupWaitBackgroundBrush"] = MediaColors.White,
                ["AppStartupWaitForegroundBrush"] = MediaColors.Black,
                ["AppStartupWaitBorderBrush"] = ColorFromRgb(0xD8DBE3),
                ["AppConnectionErrorBackgroundBrush"] = MediaColors.White,
                ["AppConnectionErrorForegroundBrush"] = ColorFromRgb(0x303133)
            }
        }
    ];

    public static ColorScheme CurrentScheme { get; private set; } = Schemes[0];

    public static void Apply(ColorScheme scheme)
    {
        CurrentScheme = scheme;
        ResourceDictionary resources = Application.Current.Resources;

        resources["AppCaptionColorValue"] = scheme.CaptionColor;
        resources["AppCaptionTextColorValue"] = scheme.CaptionTextColor;
        resources["AppStatusActiveIconKind"] = scheme.StatusActiveIconKind;

        foreach ((string key, Color color) in scheme.Colors)
        {
            resources[key] = new SolidColorBrush(color);
        }
    }

    public static void Apply(string schemeName)
    {
        ColorScheme scheme = Schemes.FirstOrDefault(item =>
            string.Equals(item.Name, schemeName, StringComparison.Ordinal)) ?? Schemes[0];
        Apply(scheme);
    }

    public static bool UsesWarningStyle(ColorScheme scheme)
    {
        return string.Equals(scheme.Name, DefaultSchemeName, StringComparison.Ordinal) ||
               string.Equals(scheme.Name, "帕琪", StringComparison.Ordinal);
    }

    private static Color ColorFromRgb(int rgb)
    {
        return Color.FromRgb(
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));
    }
}
