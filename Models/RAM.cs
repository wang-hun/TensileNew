using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
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
        public static int SaveIndex=1;
        public static Logger logger = LogManager.GetCurrentClassLogger();
        public static event Action<int> Changed;
        /// <summary>
        /// 参数设置
        /// </summary>
        public static SettingModel SettingModel { get; set; }

        public static void Init()
        {
            //读取json文件 如果不存在则创建  创建1个新的1条默认配方的json
            if (File.Exists("Setting.json"))
            {
                SettingModel = JsonConvert.DeserializeObject<SettingModel>(File.ReadAllText("Setting.json")) ?? CreateDefaultSetting();

            }
            else
            {
                SettingModel = CreateDefaultSetting();
                File.WriteAllText("Setting.json", JsonConvert.SerializeObject(SettingModel, Formatting.Indented));

            }

            EnsureValidSettings();
             
            logger.Info("加载配方参数");

        }

        private static SettingModel CreateDefaultSetting()
        {
            var recipe = new RecipeModel { RecipeName = "test1" };
            var setting = new SettingModel
            {
                CurRecipeModel = recipe
            };
            setting.RecipeModelS.Add(recipe);
            return setting;
        }

        public static void EnsureValidSettings()
        {
            SettingModel ??= CreateDefaultSetting();
            SettingModel.RecipeModelS ??= new System.ComponentModel.BindingList<RecipeModel>();

            if (SettingModel.RecipeModelS.Count == 0)
            {
                SettingModel.RecipeModelS.Add(new RecipeModel { RecipeName = "test1" });
            }

            SettingModel.CurRecipeModel ??= SettingModel.RecipeModelS[0];

            bool currentRecipeExists = SettingModel.RecipeModelS.Any(recipe =>
                string.Equals(recipe.RecipeName, SettingModel.CurRecipeModel.RecipeName, StringComparison.Ordinal));

            if (!currentRecipeExists)
            {
                SettingModel.CurRecipeModel = SettingModel.RecipeModelS[0];
            }

            if (string.IsNullOrWhiteSpace(SettingModel.ColorSchemeName) ||
                !ThemeManager.Schemes.Any(scheme => string.Equals(scheme.Name, SettingModel.ColorSchemeName, StringComparison.Ordinal)))
            {
                SettingModel.ColorSchemeName = ThemeManager.Schemes[0].Name;
            }
        }

        public static void ChangedIndex(int i)
        { 
            Changed?.Invoke(i);
        }

    }
}

