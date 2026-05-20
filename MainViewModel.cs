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
    private string _trialSerialNumber = SNModel.GetSn();
    private RecipeModel? _selectedRecipe;

    public event Action<string>? RecipeWritten;

    public MainViewModel()
    {
        DataAqc.EnsureInitialized();
        RAM.EnsureValidSettings();

        _variables = DataAqc.PLCVariables.ToDictionary(x => x.Name);
        Recipes = RAM.SettingModel.RecipeModelS;
        LoadItems = DataAqc.loadModels;
        PlcVariables = DataAqc.PLCVariables;
        _startupLanguage = RAM.SettingModel.Language;

        SNModel.LoadSN();
        _trialSerialNumber = SNModel.GetSn();

        DataAqc.LoadDataChanged += _ => OnPropertyChanged(nameof(ChartPolylinePoints));
        DataAqc.ChartCleared += () => OnPropertyChanged(nameof(ChartPolylinePoints));
        RAM.Changed += OnRecipeChanged;
        Recipes.ListChanged += Recipes_ListChanged;
        AttachRecipeHandlers();
        SelectedRecipe = FindStartupRecipe();
    }

    public BindingList<RecipeModel> Recipes { get; }
    public BindingList<Loadmodel> LoadItems { get; }
    public BindingList<PLCVariable> PlcVariables { get; }
    public SettingModel Setting => RAM.SettingModel;
    public bool IsEnglish => string.Equals(Setting.Language, "EN", StringComparison.OrdinalIgnoreCase);
    public string[] Languages { get; } = ["CN", "EN"];
    public string[] LanguageDisplayItems { get; } = ["中文", "英语"];

    public string SelectedLanguageDisplay
    {
        get => string.Equals(Setting.Language, "EN", StringComparison.OrdinalIgnoreCase) ? "英语" : "中文";
        set
        {
            string code = value == "英语" ? "EN" : "CN";
            if (!string.Equals(Setting.Language, code, StringComparison.OrdinalIgnoreCase))
            {
                Setting.Language = code;
                OnPropertyChanged();
            }
        }
    }

    public string CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public string TrialSerialNumber
    {
        get => _trialSerialNumber;
        set
        {
            if (!SetProperty(ref _trialSerialNumber, value))
            {
                return;
            }

            if (int.TryParse(value, out int sn) && sn > 0)
            {
                SNModel.SN = sn;
            }
        }
    }

    public string CurrentSn => SNModel.GetSn();

    public RecipeModel? SelectedRecipe
    {
        get => _selectedRecipe;
        set
        {
            if (!SetProperty(ref _selectedRecipe, value))
            {
                return;
            }

            if (value == null)
            {
                return;
            }

            RAM.SettingModel.CurRecipeModel = value;
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

            var items = LoadItems.TakeLast(1000).ToList();
            if (items.Count == 0)
            {
                return string.Empty;
            }

            float minX = items.Min(x => x.RealDistance);
            float maxX = items.Max(x => x.RealDistance);
            float minY = items.Min(x => x.RealForce);
            float maxY = items.Max(x => x.RealForce);
            float xRange = Math.Max(maxX - minX, 1f);
            float yRange = Math.Max(maxY - minY, 1f);

            return string.Join(" ", items.Select(item =>
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

    public async Task PulseExclusiveAsync(string variableName, string oppositeVariableName)
    {
        await SetBoolAsync(oppositeVariableName, false);
        await PulseAsync(variableName);
    }

    public Task SetBoolAsync(string variableName, bool value)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!IsPlcConnected())
                {
                    throw new InvalidOperationException("PLC未连接");
                }

                DataAqc.plc.WriteBool(Address(variableName), value);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        });
    }

    public async Task<bool> WriteRecipeAsync()
    {
        var recipe = SelectedRecipe;
        if (recipe == null)
        {
            return false;
        }

        try
        {
            if (!IsPlcConnected())
            {
                return false;
            }

            Logger.Info($"开始写入配方<{recipe.RecipeName}>数据....");
            bool verified = await Task.Run(() =>
            {
                try
                {
                    DataAqc.plc.WriteFloat(Address("冲程压边力设定"), recipe.StrokeStampingForce);
                    DataAqc.plc.WriteFloat(Address("闭环压边力设定"), recipe.ClosedLoopStampingForce);
                    DataAqc.plc.WriteFloat(Address("速度设定"), recipe.Speed);
                    DataAqc.plc.WriteFloat(Address("停机比例设定"), recipe.ShutdownRatio);
                    DataAqc.plc.WriteUShort(Address("停机延时设定"), recipe.ShutdownDelay);
                    return VerifyRecipeWritten(recipe);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                    throw;
                }
            });
            return verified;
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            return false;
        }
    }
    public bool AddRecipe(string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (Recipes.Any(x => x.RecipeName == name))
        {
            return false;
        }

        var recipe = new RecipeModel { RecipeName = name };
        Recipes.Add(recipe);
        SelectedRecipe = recipe;
        SaveSettings();
        return true;
    }
    public void DeleteRecipe()
    {
        if (SelectedRecipe == null)
        {
            return;
        }

        int selectedIndex = Recipes.IndexOf(SelectedRecipe);
        if (selectedIndex < 0)
        {
            SelectedRecipe = null;
            return;
        }

        Recipes.RemoveAt(selectedIndex);

        if (Recipes.Count == 0)
        {
            SelectedRecipe = null;
        }
        else if (selectedIndex > 0)
        {
            SelectedRecipe = Recipes[selectedIndex - 1];
        }
        else
        {
            SelectedRecipe = Recipes[0];
        }

        SaveSettings();
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
        string recipeName = SelectedRecipe?.RecipeName ?? "NoRecipe";
        var dialog = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            InitialDirectory = RAM.SettingModel.ExcelFolderPath,
            FileName = $"{recipeName}_{SNModel.GetSn()}_{DateTime.Now:yyyyMMddHHmmss}"
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
        OnPropertyChanged(nameof(CurrentSn));
        TrialSerialNumber = SNModel.GetSn();
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
        DataAqc.EnsureInitialized();
        var variable = DataAqc.PLCVariables.First(t => t.Name == variableName);
        return (ushort)ModbusAddressHelper.ConvertToModbusAddresss(variable.Address).HexAddress;
    }

    private async void OnRecipeChanged(int index)
    {
        if (bool.TryParse(DataAqc.plc?.ConnectState, out bool connected) && connected)
        {
            if (await WriteRecipeAsync() && SelectedRecipe != null)
            {
                RecipeWritten?.Invoke(SelectedRecipe.RecipeName);
            }
        }
    }

    private RecipeModel? FindStartupRecipe()
    {
        var current = RAM.SettingModel.CurRecipeModel;
        return Recipes.FirstOrDefault(recipe => string.Equals(recipe.RecipeName, current?.RecipeName, StringComparison.Ordinal))
            ?? Recipes.FirstOrDefault();
    }

    private void Recipes_ListChanged(object? sender, ListChangedEventArgs e)
    {
        AttachRecipeHandlers();
        SaveSettings();
    }

    private void AttachRecipeHandlers()
    {
        foreach (var recipe in Recipes)
        {
            recipe.PropertyChanged -= Recipe_PropertyChanged;
            recipe.PropertyChanged += Recipe_PropertyChanged;
        }
    }

    private void Recipe_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveSettings();
    }

    private static bool VerifyRecipeWritten(RecipeModel recipe)
    {
        const float tolerance = 0.01f;

        return NearlyEqual(DataAqc.plc.ReadFloat(Address("冲程压边力设定")), recipe.StrokeStampingForce, tolerance)
            && NearlyEqual(DataAqc.plc.ReadFloat(Address("闭环压边力设定")), recipe.ClosedLoopStampingForce, tolerance)
            && NearlyEqual(DataAqc.plc.ReadFloat(Address("速度设定")), recipe.Speed, tolerance)
            && NearlyEqual(DataAqc.plc.ReadFloat(Address("停机比例设定")), recipe.ShutdownRatio, tolerance)
            && DataAqc.plc.ReadUShort(Address("停机延时设定")) == recipe.ShutdownDelay;
    }

    private static bool NearlyEqual(float actual, float expected, float tolerance)
    {
        return Math.Abs(actual - expected) <= tolerance;
    }

    private static bool IsPlcConnected()
    {
        return DataAqc.plc?.Client is { Connected: true }
            && bool.TryParse(DataAqc.plc.ConnectState, out bool connected)
            && connected;
    }
}
