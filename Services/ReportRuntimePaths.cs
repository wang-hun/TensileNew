using System.IO;

namespace TensileNeW.Services;

public static class ReportRuntimePaths
{
    public static string ConfigurationDirectoryPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "ReportData");

    public static string TemporaryDirectoryPath { get; } =
        Path.Combine(ConfigurationDirectoryPath, "Temp");

    public static string EditorValuesFilePath { get; } =
        Path.Combine(ConfigurationDirectoryPath, "ReportEditorValues.json");
}