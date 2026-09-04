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
        public const string RuntimeDataSaveAlwaysYes = "始终是";
        public const string RuntimeDataSaveAskEveryTime = "每次询问";
        public const string RuntimeDataSaveAlwaysNo = "始终否";
        public const string AutoSaveAlwaysYes = "始终是";
        public const string AutoSaveAskEveryTime = "每次询问";
        public const string AutoSaveAlwaysNo = "始终否";
        public const string RuntimeDataDeleteAlwaysYes = "始终是";
        public const string RuntimeDataDeleteAskEveryTime = "每次询问";
        public const string RuntimeDataDeleteAlwaysNo = "始终否";

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

        private string _AnnotationName = string.Empty;
        public string AnnotationName
        {
            get => _AnnotationName;
            set => SetProperty(ref _AnnotationName, value);
        }

        private string _AnnotationContent = string.Empty;
        public string AnnotationContent
        {
            get => _AnnotationContent;
            set => SetProperty(ref _AnnotationContent, value);
        }

        private string _Language = "CN";
        public string Language
        {
            get => _Language;
            set => SetProperty(ref _Language, value);
        }

        private string _CameraDeviceId = string.Empty;
        public string CameraDeviceId
        {
            get => _CameraDeviceId;
            set => SetProperty(ref _CameraDeviceId, value);
        }

        private string _CameraDeviceName = string.Empty;
        public string CameraDeviceName
        {
            get => _CameraDeviceName;
            set => SetProperty(ref _CameraDeviceName, value);
        }

        /// <summary>
        /// Controls whether the optional vision module UI and behavior are enabled.
        /// The module remains part of the application when this is false.
        /// </summary>
        private bool _VisionModuleEnabled;
        public bool VisionModuleEnabled
        {
            get => _VisionModuleEnabled;
            set => SetProperty(ref _VisionModuleEnabled, value);
        }

        private bool _UseVisionDetection;
        public bool UseVisionDetection
        {
            get => _UseVisionDetection;
            set => SetProperty(ref _UseVisionDetection, value);
        }

        private string _VisionDeviceIp = "127.0.0.1";
        public string VisionDeviceIp
        {
            get => _VisionDeviceIp;
            set => SetProperty(ref _VisionDeviceIp, value);
        }

        private int _VisionDevicePort = 5000;
        public int VisionDevicePort
        {
            get => _VisionDevicePort;
            set => SetProperty(ref _VisionDevicePort, value);
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

        private string _ColorSchemeName = "帕琪";
        public string ColorSchemeName
        {
            get => _ColorSchemeName;
            set => SetProperty(ref _ColorSchemeName, value);
        }

        private string _RuntimeDataSavePolicy = RuntimeDataSaveAlwaysNo;
        public string RuntimeDataSavePolicy
        {
            get => _RuntimeDataSavePolicy;
            set => SetProperty(ref _RuntimeDataSavePolicy, value);
        }

        private string _RuntimeDataDeletePolicy = RuntimeDataDeleteAlwaysYes;
        public string RuntimeDataDeletePolicy
        {
            get => _RuntimeDataDeletePolicy;
            set => SetProperty(ref _RuntimeDataDeletePolicy, value);
        }

        private string _AutoSavePolicy = AutoSaveAskEveryTime;
        public string AutoSavePolicy
        {
            get => _AutoSavePolicy;
            set => SetProperty(ref _AutoSavePolicy, value);
        }

    }


}

