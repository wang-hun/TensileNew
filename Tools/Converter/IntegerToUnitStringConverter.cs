using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TensileNeW.Tools.Converter
{

    public class IntegerToUnitStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType)
        {
            if (value is int intValue && targetType == typeof(string))
            {
                return $"{intValue} Units"; // 添加单位
            }
            throw new InvalidOperationException("Unsupported conversion.");
        }

        public object ConvertBack(object value, Type targetType)
        {
            if (value is string stringValue && targetType == typeof(int))
            {
                if (int.TryParse(stringValue.Replace(" Units", ""), out int result))
                {
                    return result; // 去掉单位并解析为整数
                }
                throw new FormatException("Invalid input format.");
            }
            throw new InvalidOperationException("Unsupported conversion.");
        }
    }
}

