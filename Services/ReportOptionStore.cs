using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Text;
using TensileNeW.Models;

namespace TensileNeW.Services;

public sealed class ReportOptionStore
{
    public string FilePath { get; }

    public ReportOptionStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "ReportOptions.json");
    }

    public ReportOptionCatalog Load()
    {
        if (!File.Exists(FilePath))
        {
            ReportOptionCatalog defaults = ReportOptionCatalog.CreateDefault();
            Save(defaults);
            return defaults;
        }

        return ReadFromFile(FilePath, false);
    }

    public ReportOptionCatalog Import(string path) => ReadFromFile(path, true);

    private static ReportOptionCatalog ReadFromFile(string path, bool requireAllFields)
    {
        var catalog = JsonConvert.DeserializeObject<ReportOptionCatalog>(
            File.ReadAllText(path, Encoding.UTF8))
            ?? throw new InvalidDataException("报告选项配置内容无效。");
        if (catalog.Fields is null)
        {
            throw new InvalidDataException("报告选项配置缺少 Fields 字段。");
        }

        foreach (ReportOptionDefinition definition in ReportOptionCatalog.Definitions)
        {
            if (!catalog.Fields.TryGetValue(definition.Key, out var options))
            {
                if (requireAllFields)
                {
                    throw new InvalidDataException($"导入文件缺少「{definition.Title}」字段。");
                }

                catalog.Fields[definition.Key] = new(definition.DefaultOptions);
            }
            else if (options is null)
            {
                throw new InvalidDataException($"报告选项 {definition.Title} 不是有效列表。");
            }
            else if (requireAllFields && (options.Exists(string.IsNullOrWhiteSpace) ||
                options.Select(option => option.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count))
            {
                throw new InvalidDataException($"「{definition.Title}」存在空白或重复选项。");
            }
        }

        return catalog;
    }

    public void Save(ReportOptionCatalog catalog) => Export(catalog, FilePath);

    public void Export(ReportOptionCatalog catalog, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            throw new InvalidOperationException("报告选项配置路径无效。");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(catalog, Formatting.Indented),
                new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
