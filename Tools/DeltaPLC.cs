using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace TensileNeW.Tools
{

    public class DeltaPLC
    {
        private string _ipAddress;
        private int _port;
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;

        /// <summary>
        /// 构造函数，初始化 PLC 的 IP 地址和端口
        /// </summary>
        /// <param name="ipAddress">PLC 的 IP 地址</param>
        /// <param name="port">PLC 的端口号（默认为 502）</param>
        public DeltaPLC(string ipAddress, int port = 502)
        {
            _ipAddress = ipAddress;
            _port = port;
        }

        /// <summary>
        /// 连接到 PLC
        /// </summary>
        public void Connect()
        {
            try
            {
                _tcpClient = new TcpClient(_ipAddress, _port);
                _networkStream = _tcpClient.GetStream();
                Console.WriteLine("Connected to PLC successfully.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to connect to PLC: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开与 PLC 的连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                _networkStream?.Close();
                _tcpClient?.Close();
                Console.WriteLine("Disconnected from PLC.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to disconnect from PLC: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取保持寄存器（功能码 0x03）
        /// </summary>
        /// <param name="startAddress">起始地址（从 0 开始）</param>
        /// <param name="numRegisters">寄存器数量</param>
        /// <returns>读取到的数据数组</returns>
        public ushort[] ReadHoldingRegisters(ushort startAddress, ushort numRegisters)
        {
            byte[] request = BuildReadRequest(startAddress, numRegisters);
            byte[] response = SendRequest(request);

            if (response == null || response.Length < 9)
            {
                throw new Exception("Invalid response from PLC.");
            }

            ushort[] data = new ushort[numRegisters];
            for (int i = 0; i < numRegisters; i++)
            {
                data[i] = BitConverter.ToUInt16(response, 9 + i * 2);
            }

            return data;
        }

        /// <summary>
        /// 写入单个保持寄存器（功能码 0x06）
        /// </summary>
        /// <param name="address">寄存器地址（从 0 开始）</param>
        /// <param name="value">要写入的值</param>
        public void WriteSingleRegister(ushort address, ushort value)
        {
            byte[] request = BuildWriteSingleRequest(address, value);
            byte[] response = SendRequest(request);

            if (response == null || response.Length < 8)
            {
                throw new Exception("Invalid response from PLC.");
            }

            Console.WriteLine("Write single register successful.");
        }

        /// <summary>
        /// 写入多个保持寄存器（功能码 0x10）
        /// </summary>
        /// <param name="startAddress">起始地址（从 0 开始）</param>
        /// <param name="values">要写入的值数组</param>
        public void WriteMultipleRegisters(ushort startAddress, ushort[] values)
        {
            byte[] request = BuildWriteMultipleRequest(startAddress, values);
            byte[] response = SendRequest(request);

            if (response == null || response.Length < 8)
            {
                throw new Exception("Invalid response from PLC.");
            }

            Console.WriteLine("Write multiple registers successful.");
        }

        /// <summary>
        /// 构建读取保持寄存器请求报文
        /// </summary>
        private byte[] BuildReadRequest(ushort startAddress, ushort numRegisters)
        {
            byte[] request = new byte[12];
            request[0] = 0x00; // Transaction ID (high byte)
            request[1] = 0x00; // Transaction ID (low byte)
            request[2] = 0x00; // Protocol ID (high byte)
            request[3] = 0x00; // Protocol ID (low byte)
            request[4] = 0x00; // Length (high byte)
            request[5] = 0x06; // Length (low byte)
            request[6] = 0x01; // Unit ID
            request[7] = 0x03; // Function Code (0x03: Read Holding Registers)
            request[8] = (byte)(startAddress >> 8); // Start Address (high byte)
            request[9] = (byte)(startAddress & 0xFF); // Start Address (low byte)
            request[10] = (byte)(numRegisters >> 8); // Number of Registers (high byte)
            request[11] = (byte)(numRegisters & 0xFF); // Number of Registers (low byte)

            return request;
        }

        /// <summary>
        /// 构建写入单个保持寄存器请求报文
        /// </summary>
        private byte[] BuildWriteSingleRequest(ushort address, ushort value)
        {
            byte[] request = new byte[12];
            request[0] = 0x00; // Transaction ID (high byte)
            request[1] = 0x00; // Transaction ID (low byte)
            request[2] = 0x00; // Protocol ID (high byte)
            request[3] = 0x00; // Protocol ID (low byte)
            request[4] = 0x00; // Length (high byte)
            request[5] = 0x06; // Length (low byte)
            request[6] = 0x01; // Unit ID
            request[7] = 0x06; // Function Code (0x06: Write Single Register)
            request[8] = (byte)(address >> 8); // Register Address (high byte)
            request[9] = (byte)(address & 0xFF); // Register Address (low byte)
            request[10] = (byte)(value >> 8); // Value (high byte)
            request[11] = (byte)(value & 0xFF); // Value (low byte)

            return request;
        }

        /// <summary>
        /// 构建写入多个保持寄存器请求报文
        /// </summary>
        private byte[] BuildWriteMultipleRequest(ushort startAddress, ushort[] values)
        {
            int length = 7 + values.Length * 2;
            byte[] request = new byte[length];

            request[0] = 0x00; // Transaction ID (high byte)
            request[1] = 0x00; // Transaction ID (low byte)
            request[2] = 0x00; // Protocol ID (high byte)
            request[3] = 0x00; // Protocol ID (low byte)
            request[4] = (byte)((length - 6) >> 8); // Length (high byte)
            request[5] = (byte)((length - 6) & 0xFF); // Length (low byte)
            request[6] = 0x01; // Unit ID
            request[7] = 0x10; // Function Code (0x10: Write Multiple Registers)
            request[8] = (byte)(startAddress >> 8); // Start Address (high byte)
            request[9] = (byte)(startAddress & 0xFF); // Start Address (low byte)
            request[10] = (byte)(values.Length >> 8); // Number of Registers (high byte)
            request[11] = (byte)(values.Length & 0xFF); // Number of Registers (low byte)
            request[12] = (byte)(values.Length * 2); // Byte Count

            for (int i = 0; i < values.Length; i++)
            {
                request[13 + i * 2] = (byte)(values[i] >> 8); // Value (high byte)
                request[14 + i * 2] = (byte)(values[i] & 0xFF); // Value (low byte)
            }

            return request;
        }

        /// <summary>
        /// 发送请求并接收响应
        /// </summary>
        private byte[] SendRequest(byte[] request)
        {
            try
            {
                _networkStream.Write(request, 0, request.Length);
                byte[] buffer = new byte[256];
                int bytesRead = _networkStream.Read(buffer, 0, buffer.Length);
                return buffer[..bytesRead];
            }
            catch (Exception ex)
            {
                throw new Exception($"Error sending request to PLC: {ex.Message}");
            }
        }

        /// <summary>
        /// 写入浮点数到指定寄存器地址
        /// </summary>
        /// <param name="value">浮点数值</param>
        /// <param name="startRegister">起始寄存器地址</param>
        public void WriteFloat(ushort startRegister,float value)
        {
         

            byte[] bytes = BitConverter.GetBytes(value);
            //if (ReverseBytes)
                Array.Reverse(bytes);

            ushort[] registers = {
            BitConverter.ToUInt16(bytes, 0),
            BitConverter.ToUInt16(bytes, 2)
        };

            BuildWriteMultipleRequest(startRegister, registers);
        }


        /// <summary>
        /// 从指定寄存器地址读取浮点数
        /// </summary>
        public float ReadFloat(ushort startRegister)
        {
           

            // 读取两个连续寄存器值‌:ml-citation{ref="1,5" data="citationList"}
            ushort[] registers = ReadHoldingRegisters( startRegister, 2);

            // 合并为字节数组‌:ml-citation{ref="2,4" data="citationList"}
            byte[] bytes = new byte[4];
            Buffer.BlockCopy(registers, 0, bytes, 0, 4);

            // 处理端序反转‌:ml-citation{ref="1,5" data="citationList"}
            //if (ReverseBytes)
            Array.Reverse(bytes);

            return BitConverter.ToSingle(bytes, 0);
        }
    }
}

