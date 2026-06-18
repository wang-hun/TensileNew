using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TensileNeW.Models
{ 
    /// <summary>
    /// 运行全局静态数据
    /// </summary>
    public static class RAM
    {
        public const string SettingFileName = "Setting.json";
        public const string DefaultRecipeFileName = "DefaultRecipe.json";
        public static int SaveIndex=1;
        public static Logger logger = LogManager.GetCurrentClassLogger();
        public static event Action<int> Changed;
        /// <summary>
        /// 参数设置
        /// </summary>
        public static SettingModel SettingModel { get; set; }
        public static BindingList<RecipeModel> BuiltInRecipes { get; private set; } = new();

        public static void Init()
        {
            BuiltInRecipes = LoadBuiltInRecipes();

            //读取json文件 如果不存在则创建  创建1个新的1条默认配方的json
            if (File.Exists(SettingFileName))
            {
                SettingModel = JsonConvert.DeserializeObject<SettingModel>(File.ReadAllText(SettingFileName)) ?? CreateDefaultSetting();

            }
            else
            {
                SettingModel = CreateDefaultSetting();
                File.WriteAllText(SettingFileName, JsonConvert.SerializeObject(SettingModel, Formatting.Indented));

            }

            EnsureValidSettings();
             
            logger.Info("加载配方参数");

        }

        private static SettingModel CreateDefaultSetting()
        {
            var recipe = BuiltInRecipes.FirstOrDefault()?.CloneForUser() ?? RecipeModel.CreateDefault("test1");
            var setting = new SettingModel
            {
                CurRecipeModel = recipe
            };
            return setting;
        }

        public static void EnsureValidSettings()
        {
            if (BuiltInRecipes.Count == 0)
            {
                BuiltInRecipes = LoadBuiltInRecipes();
            }

            SettingModel ??= CreateDefaultSetting();
            SettingModel.RecipeModelS ??= new System.ComponentModel.BindingList<RecipeModel>();
            NormalizeUserRecipes();

            SettingModel.CurRecipeModel ??= BuiltInRecipes.FirstOrDefault()?.CloneForUser() ?? RecipeModel.CreateDefault("test1");

            bool currentRecipeExists = SettingModel.RecipeModelS.Any(recipe =>
                string.Equals(recipe.RecipeName, SettingModel.CurRecipeModel.RecipeName, StringComparison.Ordinal));

            if (!currentRecipeExists)
            {
                currentRecipeExists = BuiltInRecipes.Any(recipe =>
                    string.Equals(recipe.RecipeName, SettingModel.CurRecipeModel.RecipeName, StringComparison.Ordinal));
            }

            if (!currentRecipeExists)
            {
                SettingModel.CurRecipeModel = GetRuntimeRecipes().FirstOrDefault() ?? RecipeModel.CreateDefault("test1");
            }

            if (string.IsNullOrWhiteSpace(SettingModel.ColorSchemeName) ||
                !ThemeManager.Schemes.Any(scheme => string.Equals(scheme.Name, SettingModel.ColorSchemeName, StringComparison.Ordinal)))
            {
                SettingModel.ColorSchemeName = ThemeManager.DefaultSchemeName;
            }
        }

        public static BindingList<RecipeModel> GetRuntimeRecipes()
        {
            var recipes = new BindingList<RecipeModel>();
            foreach (var recipe in BuiltInRecipes)
            {
                recipes.Add(recipe);
            }

            foreach (var recipe in SettingModel?.RecipeModelS ?? new BindingList<RecipeModel>())
            {
                recipes.Add(recipe);
            }

            return recipes;
        }

        public static void SaveSettingModel()
        {
            if (SettingModel == null)
            {
                return;
            }

            NormalizeUserRecipes();
            File.WriteAllText(SettingFileName, JsonConvert.SerializeObject(SettingModel, Formatting.Indented));
        }

        private static void NormalizeUserRecipes()
        {
            if (SettingModel?.RecipeModelS == null)
            {
                return;
            }

            var userRecipes = SettingModel.RecipeModelS.Where(recipe => !recipe.IsBuiltInRecipe).ToList();
            if (userRecipes.Count == SettingModel.RecipeModelS.Count)
            {
                return;
            }

            SettingModel.RecipeModelS = new BindingList<RecipeModel>(userRecipes);
        }

        private static BindingList<RecipeModel> LoadBuiltInRecipes()
        {
            try
            {
                if (File.Exists(DefaultRecipeFileName))
                {
                    var recipes = JsonConvert.DeserializeObject<List<RecipeModel>>(File.ReadAllText(DefaultRecipeFileName));
                    if (recipes is { Count: > 0 })
                    {
                        foreach (var recipe in recipes)
                        {
                            recipe.IsBuiltInRecipe = true;
                        }

                        return new BindingList<RecipeModel>(recipes);
                    }
                }
            }
            catch
            {
                // Fall back to code defaults.
            }

            return new BindingList<RecipeModel>(new List<RecipeModel>
            {
                RecipeModel.CreateBuiltIn("FLC", 380, 200, 5, 0.8f, 1, RecipeModel.DefaultTensileDistanceLimit),
                RecipeModel.CreateBuiltIn("深拉试验", 108, 50, 10, 0f, 1, RecipeModel.DefaultTensileDistanceLimit),
                RecipeModel.CreateBuiltIn("铝合金冲杯", 50, 100, 10, 0.95f, 1, RecipeModel.DefaultTensileDistanceLimit),
                RecipeModel.CreateBuiltIn("杯凸", 60, 60, 8, 0.8f, 1, RecipeModel.DefaultTensileDistanceLimit)
            });
        }

        public static void ChangedIndex(int i)
        { 
            Changed?.Invoke(i);
        }

    }
}

