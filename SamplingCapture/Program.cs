using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using OfficeOpenXml;
using NModbus;
using NModbus.Device;

const string DefaultPlcIp = "192.168.1.5";
const byte SlaveId = 1;
const ushort M1Address = 0x0801;
const ushort D46Address = 0x102E;
const ushort D260Address = 0x1104;
const int DataCollectingFlagOffset = 36;
const string OutputDirectory = @"D:\data";

string plcIp = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0]
    : DefaultPlcIp;

Console.WriteLine($"连接 PLC：{plcIp}:502");
Console.WriteLine("等待 M37 数据采集标志置位。按 Ctrl+C 可退出。");

using TcpClient client = new();
using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

await client.ConnectAsync(plcIp, 502, cancellation.Token);
client.SendTimeout = 5000;
client.ReceiveTimeout = 500;

IModbusMaster master = new ModbusFactory().CreateMaster(client);
master.Transport.ReadTimeout = 100;
master.Transport.WriteTimeout = 1000;

try
{
    List<SamplePoint> samples = [];
    bool collecting = false;
    int index = 0;

    while (!cancellation.IsCancellationRequested)
    {
        bool[] coils = master.ReadCoils(SlaveId, M1Address, 80);
        bool isCollecting = coils[DataCollectingFlagOffset];

        if (!collecting)
        {
            if (!isCollecting)
            {
                continue;
            }

            samples.Clear();
            index = 0;
            collecting = true;
            Console.WriteLine("检测到 M37=True，开始采样。");
        }

        if (!isCollecting)
        {
            break;
        }

        // 与产品采集使用相同的源寄存器：D46 起 11 个 float，D260 单 float。
        float[] d46Values = ReadFloats(master, D46Address, 11);
        float distance = ReadFloat(master, D260Address);
        samples.Add(new SamplePoint(
            ++index,
            distance,
            d46Values[0],
            d46Values[4],
            d46Values[1].ToString("F3", CultureInfo.CurrentCulture)));
    }

    if (cancellation.IsCancellationRequested)
    {
        Console.WriteLine("采样已取消，未保存文件。");
        return;
    }

    string outputPath = SaveToExcel(samples);
    Console.WriteLine($"采样结束，共 {samples.Count} 点。");
    Console.WriteLine($"已保存：{outputPath}");
}
finally
{
    master.Dispose();
}

static float ReadFloat(IModbusMaster master, ushort startAddress)
{
    ushort[] registers = master.ReadHoldingRegisters(SlaveId, startAddress, 2);
    return RegistersToSingle(registers[0], registers[1]);
}

static float[] ReadFloats(IModbusMaster master, ushort startAddress, ushort count)
{
    ushort[] registers = master.ReadHoldingRegisters(SlaveId, startAddress, (ushort)(count * 2));
    float[] values = new float[count];
    for (int i = 0; i < count; i++)
    {
        values[i] = RegistersToSingle(registers[i * 2], registers[i * 2 + 1]);
    }

    return values;
}

static float RegistersToSingle(ushort lowWord, ushort highWord)
{
    byte[] bytes = new byte[4];
    BitConverter.GetBytes(lowWord).CopyTo(bytes, 0);
    BitConverter.GetBytes(highWord).CopyTo(bytes, 2);
    return BitConverter.ToSingle(bytes, 0);
}

static string SaveToExcel(IReadOnlyList<SamplePoint> samples)
{
    Directory.CreateDirectory(OutputDirectory);
    string path = Path.Combine(OutputDirectory, $"采样数据_{DateTime.Now:yyyyMMddHHmmss}.xlsx");

    using ExcelPackage package = new();
    ExcelWorksheet worksheet = package.Workbook.Worksheets.Add("Orders");
    worksheet.Cells[1, 1].Value = "序号";
    worksheet.Cells[1, 2].Value = "位移(mm)";
    worksheet.Cells[1, 3].Value = "力(kN)";
    worksheet.Cells[1, 4].Value = "压边(kN)";
    worksheet.Cells[1, 5].Value = "时间(s)";

    for (int i = 0; i < samples.Count; i++)
    {
        SamplePoint sample = samples[i];
        int row = i + 2;
        worksheet.Cells[row, 1].Value = sample.Index;
        worksheet.Cells[row, 2].Value = sample.Distance;
        worksheet.Cells[row, 3].Value = sample.Force;
        worksheet.Cells[row, 4].Value = sample.Pressure;
        worksheet.Cells[row, 5].Value = sample.Time;
    }

    package.SaveAs(new FileInfo(path));
    return path;
}

internal sealed record SamplePoint(int Index, float Distance, float Force, float Pressure, string Time);
