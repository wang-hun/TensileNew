using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using TensileNeW.Models;
using TensileNeW.Services;

namespace TensileNeW;

public sealed class ReportOptionItem : ObservableObject
{
    private string _value;

    public ReportOptionItem(string value) => _value = value;

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

public sealed class ReportOptionGroup
{
    public ReportOptionGroup(ReportOptionDefinition definition, IEnumerable<string> options)
    {
        Key = definition.Key;
        Title = definition.Title;
        TemplateCell = definition.TemplateCell;
        Description = definition.Description;
        Options = new ObservableCollection<ReportOptionItem>(options.Select(value => new ReportOptionItem(value)));
    }

    public string Key { get; }
    public string Title { get; }
    public string TemplateCell { get; }
    public string Description { get; }
    public ObservableCollection<ReportOptionItem> Options { get; }
}

public sealed class ReportOptionEditorViewModel : ObservableObject
{
    private readonly ReportOptionStore _store;
    private readonly ReportOptionCatalog _catalog;
    private ReportOptionGroup? _selectedGroup;
    private string _draftOption = string.Empty;
    private string _statusMessage = string.Empty;

    public ReportOptionEditorViewModel(ReportOptionStore store)
    {
        _store = store;
        _catalog = store.Load();
        Groups = new ObservableCollection<ReportOptionGroup>(ReportOptionCatalog.Definitions.Select(definition =>
            new ReportOptionGroup(definition, _catalog.Fields[definition.Key])));
        foreach (ReportOptionItem item in Groups.SelectMany(group => group.Options))
        {
            item.PropertyChanged += Option_PropertyChanged;
        }

        AddOptionCommand = new RelayCommand(AddOption);
        RemoveOptionCommand = new RelayCommand<ReportOptionItem>(RemoveOption);
        MoveUpCommand = new RelayCommand<ReportOptionItem>(item => MoveOption(item, -1),
            item => CanMoveOption(item, -1));
        MoveDownCommand = new RelayCommand<ReportOptionItem>(item => MoveOption(item, 1),
            item => CanMoveOption(item, 1));
        SaveCommand = new RelayCommand(Save);
        SelectedGroup = Groups.FirstOrDefault();
    }

    public ObservableCollection<ReportOptionGroup> Groups { get; }
    public string FilePath => _store.FilePath;
    public IRelayCommand AddOptionCommand { get; }
    public IRelayCommand<ReportOptionItem> RemoveOptionCommand { get; }
    public IRelayCommand<ReportOptionItem> MoveUpCommand { get; }
    public IRelayCommand<ReportOptionItem> MoveDownCommand { get; }
    public IRelayCommand SaveCommand { get; }

    public ReportOptionGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                DraftOption = string.Empty;
                RefreshMoveCommands();
            }
        }
    }

    public string DraftOption
    {
        get => _draftOption;
        set => SetProperty(ref _draftOption, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private void AddOption()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        string option = DraftOption.Trim();
        if (option.Length == 0)
        {
            StatusMessage = "请输入备选项内容。";
            return;
        }

        if (SelectedGroup.Options.Any(item => string.Equals(item.Value.Trim(), option, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "该备选项已存在。";
            return;
        }

        var added = new ReportOptionItem(option);
        added.PropertyChanged += Option_PropertyChanged;
        SelectedGroup.Options.Add(added);
        DraftOption = string.Empty;
        StatusMessage = "已新增备选项，请点击保存。";
        RefreshMoveCommands();
    }

    private void RemoveOption(ReportOptionItem? item)
    {
        if (item is null || SelectedGroup?.Options.Remove(item) != true)
        {
            return;
        }

        item.PropertyChanged -= Option_PropertyChanged;
        StatusMessage = "已删除备选项，请点击保存。";
        RefreshMoveCommands();
    }

    private bool CanMoveOption(ReportOptionItem? item, int offset)
    {
        if (item is null || SelectedGroup is null)
        {
            return false;
        }

        int position = SelectedGroup.Options.IndexOf(item);
        return position >= 0 && position + offset >= 0 && position + offset < SelectedGroup.Options.Count;
    }

    private void MoveOption(ReportOptionItem? item, int offset)
    {
        if (!CanMoveOption(item, offset))
        {
            return;
        }

        int position = SelectedGroup!.Options.IndexOf(item!);
        SelectedGroup.Options.Move(position, position + offset);
        StatusMessage = "已调整顺序，请点击保存。第一项将排在最前面。";
        RefreshMoveCommands();
    }

    private void RefreshMoveCommands()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private void Option_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        StatusMessage = "选项已编辑，请点击保存。";
    }

    public void Import(string path)
    {
        try
        {
            ReportOptionCatalog imported = _store.Import(path);
            string? selectedKey = SelectedGroup?.Key;
            foreach (ReportOptionItem item in Groups.SelectMany(group => group.Options))
            {
                item.PropertyChanged -= Option_PropertyChanged;
            }

            Groups.Clear();
            foreach (ReportOptionDefinition definition in ReportOptionCatalog.Definitions)
            {
                var group = new ReportOptionGroup(definition, imported.Fields[definition.Key]);
                foreach (ReportOptionItem item in group.Options)
                {
                    item.PropertyChanged += Option_PropertyChanged;
                }
                Groups.Add(group);
            }

            _catalog.Fields = imported.Fields;
            SelectedGroup = Groups.FirstOrDefault(group => group.Key == selectedKey) ?? Groups.FirstOrDefault();
            DraftOption = string.Empty;
            RefreshMoveCommands();
            StatusMessage = "选项已导入当前列表；点击保存后才会写入程序配置。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            StatusMessage = $"导入失败：{ex.Message}";
        }
    }

    public void Export(string path)
    {
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "导出目标不能是程序配置文件；如需保存请点击保存选项。";
            return;
        }

        if (!TryUpdateCatalog())
        {
            return;
        }

        try
        {
            _store.Export(_catalog, path);
            StatusMessage = $"当前选项已导出至：{path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"导出失败：{ex.Message}";
        }
    }

    public void ShowOperationError(string message) => StatusMessage = message;

    private void Save()
    {
        if (!TryUpdateCatalog())
        {
            return;
        }

        try
        {
            _store.Save(_catalog);
            StatusMessage = "报告下拉备选项已保存。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    private bool TryUpdateCatalog()
    {
        var updated = new Dictionary<string, List<string>>();
        foreach (ReportOptionGroup group in Groups)
        {
            List<string> options = group.Options.Select(item => item.Value?.Trim() ?? string.Empty).ToList();
            if (options.Any(string.IsNullOrWhiteSpace))
            {
                StatusMessage = $"「{group.Title}」存在空白选项，请修改或删除。";
                SelectedGroup = group;
                return false;
            }

            if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
            {
                StatusMessage = $"「{group.Title}」存在重复选项，请修改或删除。";
                SelectedGroup = group;
                return false;
            }

            updated.Add(group.Key, options);
        }

        foreach ((string key, List<string> options) in updated)
        {
            _catalog.Fields[key] = options;
        }

        return true;
    }
}
