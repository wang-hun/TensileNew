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
        
        private string _CurrentValue=string.Empty;
        public string CurrentValue
        {
            get => _CurrentValue;
            set => SetProperty(ref _CurrentValue, value);
        }


        public string WriteValue { get; set; }



    }
}

