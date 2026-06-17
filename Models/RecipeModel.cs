using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TensileNeW.Models
{
    /// <summary>
    /// 配方
    /// </summary>
    public class RecipeModel : ObservableObject
    { 
        public const float DefaultStrokeStampingForce = 100f;
        public const float DefaultClosedLoopStampingForce = 38f;
        public const ushort DefaultShutdownDelay = 10;
        public const float DefaultShutdownRatio = 0.8f;
        public const float DefaultSpeed = 1f;
        public const float DefaultTensileDistanceLimit = 100f;

        public static RecipeModel CreateDefault(string recipeName)
        {
            return new RecipeModel
            {
                RecipeName = recipeName,
                StrokeStampingForce = DefaultStrokeStampingForce,
                ClosedLoopStampingForce = DefaultClosedLoopStampingForce,
                ShutdownDelay = DefaultShutdownDelay,
                ShutdownRatio = DefaultShutdownRatio,
                Speed = DefaultSpeed,
                TensileDistanceLimit = DefaultTensileDistanceLimit
            };
        }

        public static RecipeModel CreateBuiltIn(
            string recipeName,
            float strokeStampingForce,
            float closedLoopStampingForce,
            ushort shutdownDelay,
            float shutdownRatio,
            float speed,
            float tensileDistanceLimit)
        {
            return new RecipeModel
            {
                RecipeName = recipeName,
                StrokeStampingForce = strokeStampingForce,
                ClosedLoopStampingForce = closedLoopStampingForce,
                ShutdownDelay = shutdownDelay,
                ShutdownRatio = shutdownRatio,
                Speed = speed,
                TensileDistanceLimit = tensileDistanceLimit,
                IsBuiltInRecipe = true
            };
        }

        public RecipeModel CloneForUser()
        {
            return new RecipeModel
            {
                RecipeName = RecipeName,
                StrokeStampingForce = StrokeStampingForce,
                ClosedLoopStampingForce = ClosedLoopStampingForce,
                ShutdownDelay = ShutdownDelay,
                ShutdownRatio = ShutdownRatio,
                Speed = Speed,
                TensileDistanceLimit = TensileDistanceLimit
            };
        }

        /// <summary>
        /// 内置默认配方只参与运行时列表展示，不写入用户 Setting.json。
        /// </summary>
        [JsonIgnore]
        public bool IsBuiltInRecipe { get; set; }

        /// <summary>
        ///   冲程压边力设定
        /// </summary>
        private float _StrokeStampingForce;
        public float StrokeStampingForce
        {
            get => _StrokeStampingForce;
            set => SetProperty(ref _StrokeStampingForce, value);
        }


        /// <summary>
        ///  闭环压边力设定
        /// </summary>
        private float _ClosedLoopStampingForce;
        public float ClosedLoopStampingForce
        {
            get => _ClosedLoopStampingForce;
            set => SetProperty(ref _ClosedLoopStampingForce, value);
        }

        /// <summary>
        /// 停机延时设定
        /// </summary>
        private ushort _ShutdownDelay;
        public ushort ShutdownDelay
        {
            get => _ShutdownDelay;
            set => SetProperty(ref _ShutdownDelay, value);
        }


        /// <summary>
        /// 停机比例设定
        /// </summary>
        private float _ShutdownRatio;
        public float ShutdownRatio
        {
            get => _ShutdownRatio;
            set => SetProperty(ref _ShutdownRatio, value);
        }



        /// <summary>
        /// 速度设定
        /// </summary>
        private float _Speed;
        public float Speed
        {
            get => _Speed;
            set => SetProperty(ref _Speed, value);
        }

        /// <summary>
        /// 拉伸位移上限
        /// </summary>
        private float _TensileDistanceLimit = DefaultTensileDistanceLimit;
        public float TensileDistanceLimit
        {
            get => _TensileDistanceLimit;
            set => SetProperty(ref _TensileDistanceLimit, value);
        }



        /// <summary>
        /// 配方名称
        /// </summary>
        private string _RecipeName;
        public string RecipeName
        {
            get => _RecipeName;
            set => SetProperty(ref _RecipeName, value);
        }

    }
}

