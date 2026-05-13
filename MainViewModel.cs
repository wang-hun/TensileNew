using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TensileNeW.Models;
using TensileNeW.Tools;

namespace TensileNeW;

public sealed class MainViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly Dictionary<string, PLCVariable> _variables;
    private readonly string _startupLanguage;
    private string _currentPage = "Home";

    public MainViewModel()
    {
        _variables = DataAqc.PLCVariables.ToDictionary(x => x.Name);
        Recipes = RAM.SettingModel.RecipeModelS;
        SelectedRecipe = RAM.SettingModel.CurRecipeModel;
        LoadItems = DataAqc.loadModels;
        PlcVariables = DataAqc.PLCVariables;
        _startupLanguage = RAM.SettingModel.Language;

        SNModel.LoadSN();
        CurrentSn = SNModel.GetSn();

        DataAqc.LoadDataChanged += _ => OnPropertyChanged(nameof(ChartPolylinePoints));
        DataAqc.ChartCleared += () => OnPropertyChanged(nameof(ChartPolylinePoints));
        RAM.Changed += OnRecipeChanged;
    }

    public BindingList<RecipeModel> Recipes { get; }
    public BindingList<Loadmodel> LoadItems { get; }
    public BindingList<PLCVariable> PlcVariables { get; }
    public SettingModel Setting => RAM.SettingModel;
    public bool IsEnglish => string.Equals(Setting.Language, "EN", StringComparison.OrdinalIgnoreCase);
    public string[] Languages { get; } = ["CN", "EN"];

    public string CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public string CurrentSn { get; private set; }

    public RecipeModel SelectedRecipe
    {
        get => RAM.SettingModel.CurRecipeModel;
        set
        {
            if (value == null) return;
            RAM.SettingModel.CurRecipeModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Setting));
            RAM.ChangedIndex(Recipes.IndexOf(value));
        }
    }

    public PLCVariable Variable(string name) => _variables[name];

    public string ChartPolylinePoints
    {
        get
        {
            if (LoadItems.Count == 0)
            {
                return string.Empty;
            }

            float minX = LoadItems.Min(x => x.RealDistance);
            float maxX = LoadItems.Max(x => x.RealDistance);
            float minY = LoadItems.Min(x => x.RealForce);
            float maxY = LoadItems.Max(x => x.RealForce);
            float xRange = Math.Max(maxX - minX, 1f);
            float yRange = Math.Max(maxY - minY, 1f);

            return string.Join(" ", LoadItems.Cast<Loadmodel>().TakeLast(1000).Select(item =>
            {
                double x = ((item.RealDistance - minX) / xRange) * 960 + 20;
                double y = 360 - ((item.RealForce - minY) / yRange) * 320;
                return $"{x:F1},{y:F1}";
            }));
        }
    }

    public async Task PulseAsync(string variableName)
    {
        await SetBoolAsync(variableName, true);
        await Task.Delay(100);
        await SetBoolAsync(variableName, false);
    }

    public Task SetBoolAsync(string variableName, bool value)
    {
        return Task.Run(() =>
        {
            try
            {
                DataAqc.plc.WriteBool(Address(variableName), value);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        });
    }

    public async Task WriteRecipeAsync()
    {
        try
        {
            Logger.Info($"开始写入配方<{SelectedRecipe.RecipeName}>数据....");
            await Task.Run(() =>
            {
                DataAqc.plc.WriteFloat(Address("冲程压边力设定"), SelectedRecipe.StrokeStampingForce);
                DataAqc.plc.WriteFloat(Address("闭环压边力设定"), SelectedRecipe.ClosedLoopStampingForce);
                DataAqc.plc.WriteFloat(Address("速度设定"), SelectedRecipe.Speed);
                DataAqc.plc.WriteFloat(Address("停机比例设定"), SelectedRecipe.ShutdownRatio);
                DataAqc.plc.WriteUShort(Address("停机延时设定"), SelectedRecipe.ShutdownDelay);
            });
            MessageBox.Show($"写入配方<{SelectedRecipe.RecipeName}>完成", "TensileNeW");
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            MessageBox.Show("配方参数写入失败", "TensileNeW");
        }
    }

    public void AddRecipe()
    {
        string name = $"Recipe{Recipes.Count + 1}";
        while (Recipes.Any(x => x.RecipeName == name))
        {
            name = $"Recipe{Recipes.Count + Random.Shared.Next(1, 100)}";
        }

        var recipe = new RecipeModel { RecipeName = name };
        Recipes.Add(recipe);
        SelectedRecipe = recipe;
    }

    public void DeleteRecipe()
    {
        if (Recipes.Count <= 1 || SelectedRecipe == null)
        {
            return;
        }

        int index = Math.Max(0, Recipes.IndexOf(SelectedRecipe) - 1);
        Recipes.Remove(SelectedRecipe);
        SelectedRecipe = Recipes[index];
    }

    public void SaveSettings()
    {
        File.WriteAllText("Setting.json", JsonConvert.SerializeObject(RAM.SettingModel, Formatting.Indented));
    }

    public void SaveSettingsAndApplyLanguage()
    {
        SaveSettings();
        if (!string.Equals(_startupLanguage, Setting.Language, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("语言已切换，软件即将关闭 !", "TensileNeW");
            Environment.Exit(0);
        }
    }

    public void SaveDataAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            InitialDirectory = RAM.SettingModel.ExcelFolderPath,
            FileName = $"{SelectedRecipe.RecipeName}_{SNModel.GetSn()}_{DateTime.Now:yyyyMMddHHmmss}"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        using var exporter = new ExcelExporter_EPPlus();
        exporter.CreateSheet("Orders")
            .SetHeader(new[] { "序号", "压力", "位移", "载荷", "时间" })
            .AddData(DataAqc.loadModels, o => new object[] { o.Index, o.RealPress, o.RealDistance, o.RealForce, o.Time })
            .SaveToFile(dialog.FileName);

        SNModel.WriteSN();
        CurrentSn = SNModel.GetSn();
        OnPropertyChanged(nameof(CurrentSn));
    }

    public static void StopConsumers()
    {
        DataAqc._cts.Cancel();
        while (!DataAqc._queue.IsEmpty)
        {
            Thread.Sleep(10);
        }
    }

    private static ushort Address(string variableName)
    {
        var variable = DataAqc.PLCVariables.First(t => t.Name == variableName);
        return (ushort)ModbusAddressHelper.ConvertToModbusAddresss(variable.Address).HexAddress;
    }

    private async void OnRecipeChanged(int index)
    {
        if (bool.TryParse(DataAqc.plc?.ConnectState, out bool connected) && connected)
        {
            await WriteRecipeAsync();
        }
    }
}
