using CommunityToolkit.Mvvm.ComponentModel;
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

