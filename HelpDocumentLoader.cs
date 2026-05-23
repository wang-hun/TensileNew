using System.IO;
using System.Reflection;
using System.Text.Json;
using TensileNeW.Models;

namespace TensileNeW;

public static class HelpDocumentLoader
{
    private const string HelperDirectoryName = "helper";
    private const string DefaultDocumentName = "cup-test-guide/index.html";
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
            List<HelpNavigationItem> navigation = JsonSerializer.Deserialize<List<HelpNavigationItem>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];
            foreach (HelpNavigationItem item in navigation)
            {
                item.IsRoot = true;
            }

            return navigation;
        }
        catch
        {
            return [];
        }
    }

    public static Uri? TryBuildNavigationUri(HelpNavigationItem? item)
    {
        try
        {
            string? helperDirectory = TryGetHelperDirectory();
            if (string.IsNullOrWhiteSpace(helperDirectory))
            {
                return null;
            }

            string documentName = string.IsNullOrWhiteSpace(item?.Document)
                ? DefaultDocumentName
                : item.Document;
            string documentPath = Path.Combine(
                helperDirectory,
                documentName.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(documentPath))
            {
                return null;
            }

            UriBuilder builder = new(new Uri(documentPath))
            {
                Fragment = string.IsNullOrWhiteSpace(item?.Anchor) ? string.Empty : item.Anchor
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
