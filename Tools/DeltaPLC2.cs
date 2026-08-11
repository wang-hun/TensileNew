using NModbus;
using NModbus.Device;
using NModbus.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using NLog;

namespace TensileNeW.Tools
{
    
    public class TestDeltaPLC2
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public static void test()
        {
            DeltaPLC2 plc = new DeltaPLC2("192.168.1.5");

            try
            {
                // 连接到 PLC（IP 地址为 192.168.1.1，从站 ID 为 1）
                plc.Connect();

                // 读写单个 Bool（地址 0）
                bool coilValue = plc.ReadBool(0);
                Console.WriteLine($"读取到 Bool 值: {coilValue}");
                plc.WriteBool(0, !coilValue); // 取反写入

                // 读写单个 UShort（地址 40001）
                ushort registerValue = plc.ReadUShort(40001);
                Console.WriteLine($"读取到 UShort 值: {registerValue}");
                plc.WriteUShort(40001, 12345);

                // 读写单个 Float（地址 40003 和 40004）
                float floatValue = plc.ReadFloat(40003);
                Console.WriteLine($"读取到 Float 值: {floatValue}");
                plc.WriteFloat(40003, 123.45f);

                // 读写多个 Bool（地址 0-2）
                bool[] coils = plc.ReadBools(0, 3);
                Console.WriteLine($"读取到 Bool 数组: {string.Join(", ", coils)}");
                plc.WriteBools(0, new bool[] { true, false, true });

                // 读写多个 Float（地址 40003-40006）
                float[] floats = plc.ReadFloats(40003, 2);
                Console.WriteLine($"读取到 Float 数组: {string.Join(", ", floats)}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "PLC 测试操作失败。");
                Console.WriteLine($"操作失败: {ex.Message}");
            }
            finally
            {
                plc.Disconnect();
            }
        }
    }
    public class DeltaPLC2:ObservableObject
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const int ConnectTimeoutMilliseconds = 5000;
        private IModbusMaster _master; 
        private TcpClient _client; 
        private readonly string _ipAddress;
        private readonly byte _slaveId;

        private string _ConnectState="false";
        public string ConnectState
        {
            get => _ConnectState;
            set => SetProperty(ref _ConnectState, value);
        }
        public TcpClient Client { get => _client; private set => _client = value; }


        /// <summary>
        /// 连接到台达 PLC
        /// </summary>
        /// <param name="ipAddress">PLC 的 IP 地址</param>
        /// <param name="port">Modbus 端口（默认 502）</param>
        /// <param name="slaveId">从站 ID（默认 1）</param>
        public DeltaPLC2(string ip, byte slaveId = 1)
        {
            _ipAddress = ip;
            _slaveId = slaveId;
        }

        /// <summary>
        /// 建立TCP连接‌:ml-citation{ref="1,4" data="citationList"}
        /// </summary>
        public void Connect(int port = 502)
        {
            ConnectState="false";
            TcpClient client = new();

            try
            {
                Task connectTask = client.ConnectAsync(_ipAddress, port);
                if (!connectTask.Wait(ConnectTimeoutMilliseconds))
                {
                    _ = connectTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException($"连接 PLC 超时：{_ipAddress}:{port}");
                }

                connectTask.GetAwaiter().GetResult();
                client.SendTimeout =5000;
                client.ReceiveTimeout = 500; 
                IModbusMaster master = new ModbusFactory().CreateMaster(client);
                master.Transport.ReadTimeout = 100;
                master.Transport.WriteTimeout = 1000;

                Client = client;
                _master = master;
                ConnectState = "true";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "PLC 连接初始化失败。");
                client.Close();
                ConnectState = "false";
                throw;
            }
        }

        /// <summary>
        /// 断开与 PLC 的连接
        /// </summary>
        public void Disconnect()
        {
            Client?.Close(); 
            _master?.Dispose();
            ConnectState = "false";
        }

        // === 读写单个数据 ===

        /// <summary>
        /// 读取单个线圈状态（Bool）
        /// </summary>
        /// <param name="address">地址（如 0 表示线圈地址 00001）</param>
        public bool ReadBool(ushort address)
        {
            //if (_master is null) return false;
            //if (Client.Connected==false) { throw new Exception("连接中断"); }
            if (null == Client || Client.Connected == false)
            {
                throw new Exception("连接中断");
            }

            bool[] values = _master.ReadCoils(_slaveId,address, 1);
            return values[0];
        }

        /// <summary>
        /// 写入单个线圈状态（Bool）
        /// </summary>
        /// <param name="address">地址</param>
        /// <param name="value">值</param>
        public void WriteBool(ushort address, bool value)
        {
            if (null == Client || Client.Connected == false)
            {
                throw new Exception("连接中断");
            }
            _master?.WriteSingleCoil(_slaveId, address, value);
        }

        /// <summary>
        /// 读取单个保持寄存器（UShort）
        /// </summary>
        public ushort ReadUShort(ushort address)
        {
            if (null == Client || Client.Connected == false)
            {
                throw new Exception("连接中断");
            }
            //if (_master is null) return 0;
            ushort[] values = _master.ReadHoldingRegisters(_slaveId, address, 1);
            return values[0];
        }

        /// <summary>
        /// 写入单个保持寄存器（UShort）
        /// </summary>
        public void WriteUShort(ushort address, ushort value)
        {
            if (null == Client || Client.Connected == false)
            {
                throw new Exception("连接中断");
            }
            _master?.WriteSingleRegister(_slaveId, address, value);
        }

        /// <summary>
        /// 读取单个 Float（占用 2 个寄存器）
        /// </summary>
        public float ReadFloat(ushort startAddress)
        {
            //if (_master is null) return 0;
            if (null == Client || Client.Connected == false)
            {
                throw new Exception("连接中断");
            }

            ushort[] registers = _master.ReadHoldingRegisters(_slaveId, startAddress, 2);
            byte[] bytes = new byte[4];
            BitConverter.GetBytes(registers[0]).CopyTo(bytes, 0);
            BitConverter.GetBytes(registers[1]).CopyTo(bytes, 2);
            return BitConverter.ToSingle(bytes, 0);
        }

        /// <summary>
        /// 写入单个 Float（占用 2 个寄存器）
        /// </summary>
        public void WriteFloat(ushort startAddress, float value)
        {
            if (null == Client || Client.Connected == false)
            {
                //throw new Exception("连接中断");
            }
            byte[] bytes = BitConverter.GetBytes(value);
            ushort[] registers = new ushort[2];
            registers[0] = BitConverter.ToUInt16(bytes, 0);
            registers[1] = BitConverter.ToUInt16(bytes, 2);
            _master?.WriteMultipleRegisters(_slaveId, startAddress, registers);
        }

        /// <summary>
        /// 读取单个 Int（占用 1 个寄存器，有符号）
        /// </summary>
        public int ReadInt(ushort address)
        {
            ushort registerValue = ReadUShort(address);
            return (short)registerValue; // 转换为有符号整数
        }

        /// <summary>
        /// 写入单个 Int（占用 1 个寄存器）
        /// </summary>
        public void WriteInt(ushort address, int value)
        {
            ushort ushortValue = (ushort)value;
            WriteUShort(address, ushortValue);
        }

        // === 读写多个数据 ===

        /// <summary>
        /// 读取多个线圈状态（Bool）
        /// </summary>
        public bool[] ReadBools(ushort startAddress, ushort count)
        {
            if (null == Client || Client.Connected == false)
            {
                throw new Exception("连接中断");
            }
            //if (_master is null) return new bool[count];
            return _master.ReadCoils(_slaveId, startAddress, count);
        }

        /// <summary>
        /// 写入多个线圈状态（Bool）
        /// </summary>
        public void WriteBools(ushort startAddress, bool[] values)
        {
            if (null == Client || Client.Connected == false)
            {
                throw new Exception("连接中断");
            }
            _master?.WriteMultipleCoils(_slaveId, startAddress, values);
        }

        /// <summary>
        /// 读取多个保持寄存器（UShort）
        /// </summary>
        public ushort[]? ReadUShorts(ushort startAddress, ushort count)
        {
            return _master?.ReadHoldingRegisters(_slaveId, startAddress, count);
        }

        /// <summary>
        /// 写入多个保持寄存器（UShort）
        /// </summary>
        public void WriteUShorts(ushort startAddress, ushort[] values)
        {
            _master?.WriteMultipleRegisters(_slaveId, startAddress, values);
        }

        /// <summary>
        /// 读取多个 Float（每个 Float 占用 2 个寄存器）
        /// </summary>
        public float[] ReadFloats(ushort startAddress, ushort count)
        {
            float[] floats = new float[count];
            //if (_master is null) return floats;
            if (null == Client || Client.Connected == false)
            { 
                throw new Exception("连接中断");
            }
            ushort[] registers = _master.ReadHoldingRegisters(_slaveId, startAddress, (ushort)(count * 2));
            for (int i = 0; i < count; i++)
            {
                byte[] bytes = new byte[4];
                BitConverter.GetBytes(registers[i * 2]).CopyTo(bytes, 0);
                BitConverter.GetBytes(registers[i * 2 + 1]).CopyTo(bytes, 2);
                floats[i] = BitConverter.ToSingle(bytes, 0);
            }
            return floats;
        }

        /// <summary>
        /// 写入多个 Float（每个 Float 占用 2 个寄存器）
        /// </summary>
        public void WriteFloats(ushort startAddress, float[] values)
        {
            if (_master is null) return ;
            ushort[] registers = new ushort[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                byte[] bytes = BitConverter.GetBytes(values[i]);
                registers[i * 2] = BitConverter.ToUInt16(bytes, 0);
                registers[i * 2 + 1] = BitConverter.ToUInt16(bytes, 2);
            }
            _master.WriteMultipleRegisters(_slaveId, startAddress, registers);
        }
    }
}

