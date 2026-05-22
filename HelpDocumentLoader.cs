using System.IO;
using System.Reflection;
using System.Text.Json;
using TensileNeW.Models;

namespace TensileNeW;

public static class HelpDocumentLoader
{
    private const string HelperDirectoryName = "helper";
    private const string DefaultDocumentName = "index.html";
    private const string NavigationDocumentName = "navigation.json";

    public static Uri? TryGetDefaultDocumentUri()
    {
        try
        {
            string? helperDirectory = TryGetHelperDirectory();
            if (!Directory.Exists(helperDirectory))
            {
                return null;
            }

            string htmlPath = Path.Combine(helperDirectory, DefaultDocumentName);
            if (!File.Exists(htmlPath))
            {
                return null;
            }

            return new Uri(htmlPath);
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<HelpNavigationItem> LoadNavigation()
    {
        try
        {
            string? helperDirectory = TryGetHelperDirectory();
            if (!Directory.Exists(helperDirectory))
            {
                return [];
            }

            string navigationPath = Path.Combine(helperDirectory, NavigationDocumentName);
            if (!File.Exists(navigationPath))
            {
                return [];
            }

            string json = File.ReadAllText(navigationPath);
            return JsonSerializer.Deserialize<List<HelpNavigationItem>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static Uri? TryBuildAnchorUri(Uri? documentUri, HelpNavigationItem? item)
    {
        try
        {
            if (documentUri is null || string.IsNullOrWhiteSpace(item?.Anchor))
            {
                return null;
            }

            UriBuilder builder = new(documentUri)
            {
                Fragment = item.Anchor
            };
            return builder.Uri;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetHelperDirectory()
    {
        string? exePath = Environment.ProcessPath;
        string? baseDirectory = Path.GetDirectoryName(exePath);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        return string.IsNullOrWhiteSpace(baseDirectory)
            ? null
            : Path.Combine(baseDirectory, HelperDirectoryName);
    }
}
