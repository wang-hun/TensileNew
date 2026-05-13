using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Net.Sockets;
using NModbus;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Runtime.InteropServices.JavaScript;

namespace TensileSystem.Tools
{
    public class DeltaPlcController : IDisposable
    {
        private TcpClient _tcpClient;
        private IModbusMaster _modbusMaster;
        private ModbusFactory modbusFactory = new ModbusFactory();
        private readonly string _ipAddress;
        private readonly byte _slaveId;

        /// <summary>
        /// 台达PLC默认为大端序，需启用字节反转‌:ml-citation{ref="2" data="citationList"}
        /// </summary>
        public bool ReverseBytes { get; set; } = true;

        public DeltaPlcController(string ip, byte slaveId = 1)
        {
            _ipAddress = ip;
            _slaveId = slaveId;
        }

        /// <summary>
        /// 建立TCP连接‌:ml-citation{ref="1,4" data="citationList"}
        /// </summary>
        public void Connect(int port = 502)
        {
            _tcpClient = new TcpClient(_ipAddress, port);
            _modbusMaster = modbusFactory.CreateMaster(_tcpClient);
        }

        /// <summary>
        /// 写入浮点数到保持寄存器‌:ml-citation{ref="1,2" data="citationList"}
        /// </summary>
        public void WriteFloat(ushort startAddress, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (ReverseBytes) Array.Reverse(bytes);

            ushort[] registers = {
            BitConverter.ToUInt16(bytes, 0),
            BitConverter.ToUInt16(bytes, 2)
        };
            _modbusMaster.WriteMultipleRegisters(_slaveId, startAddress, registers);
        }

        /// <summary>
        /// 读取保持寄存器中的浮点数‌:ml-citation{ref="2,3" data="citationList"}
        /// </summary>
        public float ReadFloat(ushort startAddress)
        {
            ushort[] registers = _modbusMaster.ReadHoldingRegisters(_slaveId, startAddress, 2);
            byte[] bytes = new byte[4];
            Buffer.BlockCopy(registers, 0, bytes, 0, 4);
            if (ReverseBytes) Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

        /// <summary>
        /// 批量读取多个float值‌:ml-citation{ref="1,2" data="citationList"}
        /// </summary>
        public float[] ReadFloats(ushort startRegister, ushort count, bool isBigEndian = true)
        {
            ushort[] registers = _modbusMaster.ReadHoldingRegisters(_slaveId, startRegister, (ushort)(count * 2));
            float[] results = new float[count];
            for (int i = 0; i < count; i++)
            {
                results[i] = ConvertRegistersToFloat(new[] { registers[i * 2], registers[i * 2 + 1] }, isBigEndian);
            }
            return results;
        }

        /// <summary>
        /// 写入单个float值（拆分到两个连续寄存器）‌:ml-citation{ref="1,2" data="citationList"}
        /// </summary>
        public void WriteFloat(ushort startRegister, float value, bool isBigEndian = true)
        {
            ushort[] registers = SplitFloatToRegisters(value, isBigEndian);
            _modbusMaster.WriteMultipleRegisters(_slaveId, startRegister, registers);
        }

        /// <summary>
        /// 批量写入多个float值‌:ml-citation{ref="1,2" data="citationList"}
        /// </summary>
        public void WriteFloats(ushort startRegister, float[] values, bool isBigEndian = true)
        {
            ushort[] registers = new ushort[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                ushort[] parts = SplitFloatToRegisters(values[i], isBigEndian);
                registers[i * 2] = parts[0];
                registers[i * 2 + 1] = parts[1];
            }
            _modbusMaster.WriteMultipleRegisters(_slaveId, startRegister, registers);
        }

        // 将两个寄存器合并为float（支持端序）‌:ml-citation{ref="1,2" data="citationList"}

        
        private float ConvertRegistersToFloat(ushort[] registers, bool isBigEndian)
        {
            byte[] bytes = new byte[4];
            if (isBigEndian)
            {
                Buffer.BlockCopy(registers, 0, bytes, 0, 4);
            }
            //else
            //{
            //    bytes[] = (byte)(registers‌[1] >> 8);
            //bytes[1] = (byte)(registers[1]&0xFF);
            //bytes‌[5] = (byte)(registers >> 8);
            //bytes‌[3] = (byte)(registers & 0xFF);
            //}
            return BitConverter.ToSingle(bytes, 0);
        }

        //// 小端序：registers是高16位，registers是低16位
        //bytes = (byte) (registers & 0xFF); // 低8位
        //        bytes = (byte) (registers >> 8);   // 高8位
        //        bytes = (byte) (registers & 0xFF); // 低8位
        //        bytes = (byte) (registers >> 8);   // 高8位


        // 将float拆分为两个寄存器（支持端序）‌:ml-citation{ref="1,2" data="citationList"}
        private ushort[] SplitFloatToRegisters(float value, bool isBigEndian)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            ushort[] registers = new ushort‌[4];
            //if (isBigEndian)
            //{
       
            registers = (ushort)((bytes << 8) | bytes[0];
            registers‌: ml - citation{ ref= "1" data = "citationList"} = (ushort)((bytes‌: ml - citation{ ref= "5" data = "citationList"} << 8) | bytes‌:ml - citation{ ref= "3" data = "citationList"});
            //}
            //else
            //{
            //    registers = (ushort)((bytes‌: ml - citation{ ref= "5" data = "citationList"} << 8) | bytes‌:ml - citation{ ref= "3" data = "citationList"});
            //registers‌: ml - citation{ ref= "1" data = "citationList"} = (ushort)((bytes << 8) | bytes‌:ml - citation{ ref= "1" data = "citationList"});
            //}
            return registers;
        }






        /// <summary>
        /// 读取单个保持寄存器的ushort值（地址范围400001-465536）‌:ml-citation{ref="1,3" data="citationList"}
        /// </summary>
        public ushort ReadUshort(ushort registerAddress)
        {
            ushort[] registers = _modbusMaster.ReadHoldingRegisters(_slaveId, registerAddress, 1);
            return registers[0]; // 直接返回单个寄存器的值‌:ml-citation{ref="3" data="citationList"}
        }

        /// <summary>
        /// 批量读取保持寄存器的ushort数组‌:ml-citation{ref="3,5" data="citationList"}
        /// </summary>
        public ushort[] ReadUshorts(ushort startAddress, ushort count)
        {
            return _modbusMaster.ReadHoldingRegisters(_slaveId, startAddress, count);
        }

        /// <summary>
        /// 写入单个保持寄存器的ushort值‌:ml-citation{ref="1,5" data="citationList"}
        /// </summary>
        public void WriteUshort(ushort registerAddress, ushort value)
        {
            _modbusMaster.WriteSingleRegister(_slaveId, registerAddress, value);
        }

        /// <summary>
        /// 批量写入保持寄存器的ushort数组‌:ml-citation{ref="3,5" data="citationList"}
        /// </summary>
        public void WriteUshorts(ushort startAddress, ushort[] values)
        {
            _modbusMaster.WriteMultipleRegisters(_slaveId, startAddress, values);
        }




        /// <summary>
        /// 读取单个线圈状态（可读写，地址范围000001-099999）‌:ml-citation{ref="1,3" data="citationList"}
        /// </summary>
        public bool ReadCoil(ushort coilAddress)
        {
            bool[] coils = _modbusMaster.ReadCoils(_slaveId, coilAddress, 1); // ‌:ml-citation{ref="2,3" data="citationList"}
            return coils[0];
        }

        public bool[] ReadCoils(ushort coilAddress, ushort count)
        {
            return _modbusMaster.ReadCoils(_slaveId, coilAddress, count); // ‌:ml-citation{ref="2,3" data="citationList"}
             
        }

        /// <summary>
        /// 读取单个离散输入状态（只读，地址范围100001-199999）‌:ml-citation{ref="2,3" data="citationList"}
        /// </summary>
        public bool ReadDiscreteInput(ushort inputAddress)
        {
            bool[] inputs = _modbusMaster.ReadInputs(_slaveId, inputAddress, 1); // ‌:ml-citation{ref="2,3" data="citationList"}
            return inputs[0];
        }

        public bool[] ReadCountBoolInputs(ushort inputAddress,ushort count)
        {
            return  _modbusMaster.ReadInputs(_slaveId, inputAddress, count); // ‌:ml-citation{ref="2,3" data="citationList"}
             
        }

        /// <summary>
        /// 写入单个线圈状态（功能码05）‌:ml-citation{ref="2,3" data="citationList"}
        /// </summary>
        public void WriteCoil(ushort coilAddress, bool value)
        {
            _modbusMaster.WriteSingleCoil(_slaveId, coilAddress, value); // ‌:ml-citation{ref="2,3" data="citationList"}
        }

        /// <summary>
        /// 批量写入多个线圈状态（功能码15）‌:ml-citation{ref="2,3" data="citationList"}
        /// </summary>
        public void WriteCoils(ushort startAddress, bool[] values)
        {
            _modbusMaster.WriteMultipleCoils(_slaveId, startAddress, values); // ‌:ml-citation{ref="2,3" data="citationList"}
        }


        /// <summary>
        /// 读取单个int值（占用两个连续保持寄存器）‌:ml-citation{ref="1,2" data="citationList"}
        /// </summary>
        public int ReadInt(ushort startRegister, bool isBigEndian = true)
        {
            ushort[] registers = _modbusMaster.ReadHoldingRegisters(_slaveId, startRegister, 2);
            return CombineRegistersToInt(registers, isBigEndian);
        }

        /// <summary>
        /// 批量读取多个int值‌:ml-citation{ref="2,3" data="citationList"}
        /// </summary>
        public int[] ReadInts(ushort startRegister, ushort count, bool isBigEndian = true)
        {
            ushort[] registers = _modbusMaster.ReadHoldingRegisters(_slaveId, startRegister, (ushort)(count * 2));
            int[] results = new int[count];
            for (int i = 0; i < count; i++)
            {
                results[i] = CombineRegistersToInt(new[] { registers[i * 2], registers[i * 2 + 1] }, isBigEndian);
            }
            return results;
        }

        /// <summary>
        /// 写入单个int值（拆分到两个连续寄存器）‌:ml-citation{ref="1,2" data="citationList"}
        /// </summary>
        public void WriteInt(ushort startRegister, int value, bool isBigEndian = true)
        {
            ushort[] registers = SplitIntToRegisters(value, isBigEndian);
            _modbusMaster.WriteMultipleRegisters(_slaveId, startRegister, registers);
        }

        /// <summary>
        /// 批量写入多个int值‌:ml-citation{ref="2,3" data="citationList"}
        /// </summary>
        public void WriteInts(ushort startRegister, int[] values, bool isBigEndian = true)
        {
            ushort[] registers = new ushort[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                ushort[] parts = SplitIntToRegisters(values[i], isBigEndian);
                registers[i * 2] = parts[0];
                registers[i * 2 + 1] = parts‌[1];
            }
            _modbusMaster.WriteMultipleRegisters(_slaveId, startRegister, registers);
        }

        // 合并两个寄存器为int（支持端序）
        private int CombineRegistersToInt(ushort[] registers, bool isBigEndian)
        {
            if (isBigEndian)
                return (registers[0] << 16) | registers‌[1];
            else
                return (registers[1] << 16) | registers[0];
        }

        // 拆分int为两个寄存器（支持端序）
        private ushort[] SplitIntToRegisters(int value, bool isBigEndian)
        {
            ushort[] registers = new ushort[2];
            if (isBigEndian)
            {
                registers[0] = (ushort)(value >> 16);
                registers[1] = (ushort)(value & 0xFFFF);
            }
            else
            {
                registers[1] = (ushort)(value >> 16);
                registers[0] = (ushort)(value & 0xFFFF);
            }
            return registers;
        }
        public void Dispose()
        {
            _modbusMaster?.Dispose();
            _tcpClient?.Close();
        }
    }

}
