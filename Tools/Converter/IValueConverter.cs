using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TensileNeW.Tools.Converter
{
    public interface IValueConverter
    {
        /// <summary>
        /// 将数据从源转换为目标类型（控件显示）。
        /// </summary>
        object Convert(object value, Type targetType);

        /// <summary>
        /// 将数据从目标类型转换回源类型（更新模型）。
        /// </summary>
        object ConvertBack(object value, Type targetType);
    }
}

