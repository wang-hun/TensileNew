using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TensileNeW.Models
{
    public class Loadmodel
    {
        public int Index { get; set; }
        /// <summary>
        ///    ‌D54‌ 实时压边力
        /// </summary>
        public float RealPress { get; set; }

        /// <summary>
        /// ‌D260‌ 实时拉伸位移
        /// </summary>
        public float RealDistance { get; set; }

        /// <summary>
        /// ‌D46‌ 实时拉伸力
        /// </summary>
        public float RealForce { get; set; }

        public string Time { get; set; }


        //        D54 实时压边力   数据读
        //        D249    实时拉伸速度 数据读
        //D260 实时拉伸位移  数据读
        //D46 实时拉伸力 数据读

        //            ‌D54‌ 实时压边力 数据读 Real-time Blankholder Force Data Read
        //‌D249‌ 实时拉伸速度 数据读 Real-time Drawing Speed Data Read
        //‌D260‌ 实时拉伸位移 数据读 Real-time Drawing Displacement Data Read
        //‌D46‌ 实时拉伸力 数据读 Real-time Drawing Force Data Read


    }
}

