using System.Collections.Generic;
using System.Windows.Media;

namespace TensileNeW.Models;

public sealed class ColorScheme
{
    public required string Name { get; init; }
    public required Color CaptionColor { get; init; }
    public required Color CaptionTextColor { get; init; }
    public required Dictionary<string, Color> Colors { get; init; }
}
