using System.Collections.ObjectModel;

namespace TensileNeW.Models;

public sealed class HelpNavigationItem
{
    public string Title { get; set; } = string.Empty;
    public string? Document { get; set; }
    public string? Anchor { get; set; }
    public string? FilePath { get; set; }
    public string? CachedPath { get; set; }
    public string? UnavailableMessage { get; set; }
    public bool IsRoot { get; set; }
    public bool IsManualFile { get; set; }
    public bool IsUnavailable { get; set; }
    public ObservableCollection<HelpNavigationItem> Children { get; set; } = [];
}
