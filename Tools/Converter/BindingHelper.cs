using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TensileNeW.Tools.Converter
{
    //public class String2DoubleDateFormatter : IFormatProvider  , ICustomFormatter
    //{
    //    public object GetFormat(Type formatType)
    //    {
    //        return  formatType == typeof(ICustomFormatter) ? this : null;
    //    }

    //    public string Format(string format, object arg, IFormatProvider formatProvider)
    //    {
    //        //if (arg is DateTime date)
    //        //    return date.ToString("yyyy-MM-dd");
    //        return arg?.ToString() ?? string.Empty;

    //    }
    //}
     

    public static class BindingHelper
    {
        /// <summary>
        /// 创建一个带有转换器的数据绑定。
        /// </summary>
        /// <param name="controlProperty">控件的属性名称（如 "Text"）。</param>
        /// <param name="dataSource">数据源对象。</param>
        /// <param name="dataMember">数据源的属性名称。</param>
        /// <param name="converter">值转换器。</param>
        /// <returns>配置好的 Binding 对象。</returns>
        public static Binding CreateBinding(string controlProperty, object dataSource, string dataMember, IValueConverter converter)
        {
            var binding = new Binding(controlProperty, dataSource, dataMember, true, DataSourceUpdateMode.OnPropertyChanged);

            // 设置 Format 事件
            binding.Format += (sender, e) =>
            {
                e.Value = converter.Convert(e.Value, e.DesiredType);
            };

            // 设置 Parse 事件
            binding.Parse += (sender, e) =>
            {
                try
                {
                    e.Value = converter.ConvertBack(e.Value, e.DesiredType);
                }
                catch (Exception ex)
                {
                    //MessageBox.Show($"Conversion error: {ex.Message}");
                    e.Value = binding.DataSource.GetType().GetProperty(dataMember).GetValue(binding.DataSource); // 恢复原始值
                }
            };

            return binding;
        }
    }


}

