using System.Collections.ObjectModel;

namespace TensileNeW.Models;

public sealed class HelpNavigationItem
{
    public string Title { get; set; } = string.Empty;
    public string? Anchor { get; set; }
    public ObservableCollection<HelpNavigationItem> Children { get; set; } = [];
}
