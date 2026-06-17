using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TensileNeW.Models
{
    public class SettingModel: ObservableObject
    { 
        /// <summary>
        /// 配方列表
        /// </summary>
        public BindingList<RecipeModel> RecipeModelS { get; set; } = new BindingList<RecipeModel>();

        /// <summary>
        /// 当前配方
        /// </summary> 

        //private RecipeModel _CurRecipeModel;
        //public RecipeModel CurRecipeModel
        //{
        //    get => _CurRecipeModel;
        //    set => SetProperty(ref _CurRecipeModel, value);
        //}
         
        public RecipeModel CurRecipeModel { get; set; } = RecipeModel.CreateDefault("test1");

       
        private string _PLC_IP="192.168.1.5";
        public string PLC_IP
        {
            get => _PLC_IP;
            set => SetProperty(ref _PLC_IP, value);
        }

        private string _ExcelFolderPath = "D:\\Data";
        public string ExcelFolderPath
        {
            get => _ExcelFolderPath;
            set => SetProperty(ref _ExcelFolderPath, value);
        }

        private string _Language = "CN";
        public string Language
        {
            get => _Language;
            set => SetProperty(ref _Language, value);
        }

        private bool _HideChartHintOnStartup;
        public bool HideChartHintOnStartup
        {
            get => _HideChartHintOnStartup;
            set => SetProperty(ref _HideChartHintOnStartup, value);
        }

        private bool _AutoTrackLatestPoint = true;
        public bool AutoTrackLatestPoint
        {
            get => _AutoTrackLatestPoint;
            set => SetProperty(ref _AutoTrackLatestPoint, value);
        }

        private bool _ShowPlotLegend = true;
        public bool ShowPlotLegend
        {
            get => _ShowPlotLegend;
            set => SetProperty(ref _ShowPlotLegend, value);
        }

        private bool _KeepPlotOnReset = true;
        public bool KeepPlotOnReset
        {
            get => _KeepPlotOnReset;
            set => SetProperty(ref _KeepPlotOnReset, value);
        }

        private string _ColorSchemeName = "警戒";
        public string ColorSchemeName
        {
            get => _ColorSchemeName;
            set => SetProperty(ref _ColorSchemeName, value);
        }

    }


}

