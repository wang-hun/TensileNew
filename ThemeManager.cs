using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using TensileNeW.Models;
using MediaColors = System.Windows.Media.Colors;

namespace TensileNeW;

public static class ThemeManager
{
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
                ["AppReconnectBackgroundBrush"] = ColorFromRgb(0xD4D5CF),
                ["AppReconnectForegroundBrush"] = ColorFromRgb(0x5CAB8C),
                ["AppReconnectHoverForegroundBrush"] = ColorFromRgb(0xFBFE8D),
                ["AppReconnectPressedForegroundBrush"] = MediaColors.Black,
                ["AppReconnectBorderBrush"] = ColorFromRgb(0xFBFE8D),
                ["AppConnectionLabelBrush"] = MediaColors.White,
                ["AppConnectionBadgeBackgroundBrush"] = ColorFromRgb(0xD4D5CF)
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
                ["AppReconnectBackgroundBrush"] = MediaColors.White,
                ["AppReconnectForegroundBrush"] = ColorFromRgb(0x1677FF),
                ["AppReconnectHoverForegroundBrush"] = ColorFromRgb(0x1677FF),
                ["AppReconnectPressedForegroundBrush"] = MediaColors.Black,
                ["AppReconnectBorderBrush"] = ColorFromRgb(0x1677FF),
                ["AppConnectionLabelBrush"] = MediaColors.Black,
                ["AppConnectionBadgeBackgroundBrush"] = MediaColors.White
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

    private static Color ColorFromRgb(int rgb)
    {
        return Color.FromRgb(
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));
    }
}
