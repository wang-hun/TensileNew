using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TensileNeW.Models
{
   
    /// <summary>
    /// PLC 变量
    /// </summary>
    public class PLCVariable:ObservableObject
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public string DataType { get; set; }
        public string DisplayDataType => DataType switch
        {
            "Boolean" => "开关量",
            "Float" => "浮点数",
            "Word" => "字（16位）",
            _ => DataType
        };
        
        private string _CurrentValue=string.Empty;
        public string CurrentValue
        {
            get => _CurrentValue;
            set
            {
                if (SetProperty(ref _CurrentValue, value))
                    OnPropertyChanged(nameof(DisplayValue));
            }
        }


        public string DisplayValue => bool.TryParse(CurrentValue, out bool value)
            ? value ? "1/开" : "0/关"
            : CurrentValue;

        public string WriteValue { get; set; }



    }
}

