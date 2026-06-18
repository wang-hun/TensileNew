using System.Collections.Generic;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace TensileNeW.Models;

public sealed class ColorScheme
{
    public required string Name { get; init; }
    public required Color CaptionColor { get; init; }
    public required Color CaptionTextColor { get; init; }
    public required PackIconBootstrapIconsKind StatusActiveIconKind { get; init; }
    public required string LogoResourcePath { get; init; }
    public required Color StartupWaitAccentColor { get; init; }
    public required Dictionary<string, Color> Colors { get; init; }
}
