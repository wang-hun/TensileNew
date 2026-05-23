using System.Collections.ObjectModel;

namespace TensileNeW.Models;

public sealed class HelpNavigationItem
{
    public string Title { get; set; } = string.Empty;
    public string? Document { get; set; }
    public string? Anchor { get; set; }
    public bool IsRoot { get; set; }
    public ObservableCollection<HelpNavigationItem> Children { get; set; } = [];
}
