using System.Collections.Generic;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TensileNeW.Models;
using MediaColors = System.Windows.Media.Colors;

namespace TensileNeW;

public static class ThemeManager
{
    public const string DefaultSchemeName = "早苗";

    public static IReadOnlyList<ColorScheme> Schemes { get; } =
    [
        new()
        {
            Name = "早苗",
            CaptionColor = ColorFromRgb(0x5CAB8C),
            CaptionTextColor = ColorFromRgb(0xD4D5CF),
            Colors = new Dictionary<string, Color>
            {
                ["AppHeaderBackgroundBrush"] = ColorFromRgb(0x5CAB8C),
                ["AppHeaderBorderBrush"] = ColorFromRgb(0xD8DBE3),
                ["AppNavBackgroundBrush"] = ColorFromRgb(0x5CAB8C),
                ["AppNavForegroundBrush"] = MediaColors.White,
                ["AppNavSelectedForegroundBrush"] = ColorFromRgb(0xFBFE8D),
                ["AppSidebarBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppSidebarLabelForegroundBrush"] = ColorFromRgb(0x005BC2),
                ["AppMetricNameForegroundBrush"] = ColorFromRgb(0x5CAB8C),
                ["AppMetricValueForegroundBrush"] = ColorFromRgb(0x005BC2),
                ["AppHomeCenterBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppLayoutBorderBrush"] = ColorFromRgb(0xFBFE8D),
                ["AppReconnectBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppReconnectForegroundBrush"] = ColorFromRgb(0x5CAB8C),
                ["AppReconnectHoverForegroundBrush"] = ColorFromRgb(0xFBFE8D),
                ["AppReconnectPressedForegroundBrush"] = MediaColors.Black,
                ["AppReconnectBorderBrush"] = ColorFromRgb(0xFBFE8D),
                ["AppConnectionLabelBrush"] = MediaColors.White,
                ["AppConnectionBadgeBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppStartupWaitBackgroundBrush"] = ColorFromRgb(0x5CAB8C),
                ["AppStartupWaitForegroundBrush"] = MediaColors.White,
                ["AppStartupWaitBorderBrush"] = ColorFromRgb(0xFBFE8D),
                ["AppConnectionErrorBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppConnectionErrorForegroundBrush"] = ColorFromRgb(0xD03050)
            }
        },
        new()
        {
            Name = "论纯白",
            CaptionColor = ColorFromRgb(0xFFFFFF),
            CaptionTextColor = ColorFromRgb(0x000000),
            Colors = new Dictionary<string, Color>
            {
                ["AppHeaderBackgroundBrush"] = ColorFromRgb(0xF5F6F8),
                ["AppHeaderBorderBrush"] = ColorFromRgb(0xD8DBE3),
                ["AppNavBackgroundBrush"] = ColorFromRgb(0xF5F6F8),
                ["AppNavForegroundBrush"] = ColorFromRgb(0x606266),
                ["AppNavSelectedForegroundBrush"] = ColorFromRgb(0x1677FF),
                ["AppSidebarBackgroundBrush"] = MediaColors.White,
                ["AppSidebarLabelForegroundBrush"] = ColorFromRgb(0x404040),
                ["AppMetricNameForegroundBrush"] = ColorFromRgb(0x696969),
                ["AppMetricValueForegroundBrush"] = MediaColors.Black,
                ["AppHomeCenterBackgroundBrush"] = MediaColors.White,
                ["AppLayoutBorderBrush"] = ColorFromRgb(0xC0C4CC),
                ["AppReconnectBackgroundBrush"] = MediaColors.White,
                ["AppReconnectForegroundBrush"] = ColorFromRgb(0x1677FF),
                ["AppReconnectHoverForegroundBrush"] = ColorFromRgb(0x1677FF),
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

    private static Color ColorFromRgb(int rgb)
    {
        return Color.FromRgb(
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));
    }
}
