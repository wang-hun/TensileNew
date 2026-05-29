using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TensileNeW.Tools
{
    public class ModbusAddress
    {
        public int DecAddress { get; set; }
        public int HexAddress { get; set; }
    }
    
    public   class ModbusAddressHelper
    {
        public static ModbusAddress ConvertToModbusAddresss(string deltAddress)
        { 
            var deltType= deltAddress.Trim().First();
            int address=int.Parse( deltAddress.Trim().Substring(1,deltAddress.Length-1));

            var modAddress = new ModbusAddress();
            switch (deltType)
            {
                case 'S':
                    {
                        modAddress.DecAddress = address + 1;
                        modAddress.HexAddress = address;
                        break;
                    }
                case 'X':
                    { 
                        modAddress.DecAddress = address + 101025;
                        modAddress.HexAddress = address+0x0400;
                        break;
                    }
                case 'Y':
                    {
                        modAddress.DecAddress = address + 1281;
                        modAddress.HexAddress = address + 0x0500;
                        break; 
                    }
                case 'T':
                    {
                        modAddress.DecAddress = address + 401537;
                        modAddress.HexAddress = address + 0x0600;
                        break;
                    }
                case 'M':
                    { 
                        modAddress.DecAddress = address + 2049;
                        modAddress.HexAddress = address + 0x0800;
                        break;
                    }
                case 'C':
                    {
                        modAddress.DecAddress = address + 3585;
                        modAddress.HexAddress = address + 0x0E00;
                        break;
                    }
                    
                case 'D':
                    {
                        modAddress.DecAddress = address + 404097;
                        modAddress.HexAddress = address + 0x1000;
                        break;
                    }
            }
            return modAddress;
        }
    }
}

