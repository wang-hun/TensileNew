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
                SettingModel = JsonConvert.DeserializeObject<SettingModel>(File.ReadAllText("Setting.json"));

            }
            else
            {
                var recipe = new RecipeModel();
                recipe.RecipeName = "test1"; 
                SettingModel = new SettingModel();
                SettingModel.CurRecipeModel = recipe;
               
                SettingModel.RecipeModelS.Add(recipe);
                File.WriteAllText("Setting.json", JsonConvert.SerializeObject(SettingModel, Formatting.Indented));

            }
             
            logger.Info("加载配方参数");

        }

        public static void ChangedIndex(int i)
        { 
            Changed?.Invoke(i);
        }

    }
}

