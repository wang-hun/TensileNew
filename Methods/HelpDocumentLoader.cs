using System.IO;
using System.Reflection;
using System.Text.Json;
using TensileNeW.Models;
using TensileNeW.Services;

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
            return File.Exists(htmlPath) ? new Uri(htmlPath) : null;
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<HelpNavigationItem> LoadNavigation()
    {
        List<HelpNavigationItem> navigation = [];
        try
        {
            string? helperDirectory = TryGetHelperDirectory();
            if (Directory.Exists(helperDirectory))
            {
                string navigationPath = Path.Combine(helperDirectory, NavigationDocumentName);
                if (File.Exists(navigationPath))
                {
                    string json = File.ReadAllText(navigationPath);
                    navigation = JsonSerializer.Deserialize<List<HelpNavigationItem>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? [];

                    foreach (HelpNavigationItem item in navigation)
                    {
                        item.IsRoot = true;
                    }
                }
            }
        }
        catch
        {
            navigation = [];
        }

        navigation.AddRange(ManualDocumentService.LoadManualNavigation());
        return navigation;
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
        string? baseDirectory = TryGetBaseDirectory();
        return string.IsNullOrWhiteSpace(baseDirectory)
            ? null
            : Path.Combine(baseDirectory, HelperDirectoryName);
    }

    private static string? TryGetBaseDirectory()
    {
        string? exePath = Environment.ProcessPath;
        string? baseDirectory = Path.GetDirectoryName(exePath);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        return baseDirectory;
    }
}
