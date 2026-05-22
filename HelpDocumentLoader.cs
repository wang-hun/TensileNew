using System.IO;
using System.Reflection;

namespace TensileNeW;

public static class HelpDocumentLoader
{
    private const string HelperDirectoryName = "helper";
    private const string DefaultDocumentName = "index.html";

    public static Uri? TryGetDefaultDocumentUri()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            string? baseDirectory = Path.GetDirectoryName(exePath);
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }

            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return null;
            }

            string helperDirectory = Path.Combine(baseDirectory, HelperDirectoryName);
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
}
