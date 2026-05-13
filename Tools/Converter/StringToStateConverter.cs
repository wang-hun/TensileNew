using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks; 

namespace TensileNeW.Tools.Converter
{

    public class StringToStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType)
        {
            if (value !=null && ! string.IsNullOrEmpty(value.ToString()))
            {
                bool parseOk= bool.TryParse(value.ToString(), out bool result);
                if (parseOk)
                {
                    return  result ? UILightState.On:UILightState.Off;
                }
                else
                {
                    return UILightState.Off;
                }
            }
            return UILightState.Off;
             
        }

        public object ConvertBack(object value, Type targetType)
        {
            if (value is UILightState stateValue && targetType == typeof(string))
            {
                
                if (stateValue == UILightState.On)
                    return "True";
                else
                    return "False";

                 
            }
            throw new InvalidOperationException("Unsupported conversion.");
        }
    }
}

