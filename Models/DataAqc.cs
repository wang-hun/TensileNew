using CommunityToolkit.Mvvm.ComponentModel;
using Haukcode.HighResolutionTimer;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using TensileNeW.Services;
using TensileNeW.Tools;

namespace TensileNeW.Models
{
    public class DataAqc
    {
        public static Logger logger = LogManager.GetCurrentClassLogger();


        public static BindingList<PLCVariable> PLCVariables = new BindingList<PLCVariable>();
        public static DeltaPLC2 plc { get; private set; }

        private static readonly ConcurrentQueue<AcquisitionBatchItem> AcquisitionQueue = new();
        public static CancellationTokenSource _cts = new CancellationTokenSource();
        public static BatchBindingList<Loadmodel> loadModels = new();
        private static readonly object PlcConnectionLock = new();

        public static event Action<IReadOnlyList<Loadmodel>>? LoadDataBatchChanged;
        public static event Action ChartCleared;
        public static event Action? DataCollectionEnded;
        public static bool _simlatueRunFlag = false;

        /// <summary>
        /// 网络探测期间置 true，<see cref="Refresh"/> 循环里的自动重连会跳过本轮调用，
        /// 避免与主窗口探测流程同时争用 PLC 连接锁或旧 socket 状态。探测结束后必须恢复为 false。
        /// </summary>
        public static volatile bool AutoReconnectSuspended = false;

        /// <summary>
        /// 初始化本地状态（PLC 变量表和 plc 客户端对象），不发起网络连接。
        /// 连接请在后台线程通过 <see cref="TryConnect"/> 调用。
        /// </summary>
        public static void InitVariables()
        {
            EnsureInitialized();

            plc = new DeltaPLC2(RAM.SettingModel.PLC_IP);
        }

        public static void EnsureInitialized()
        {
            if (PLCVariables is { Count: > 0 })
            {
                return;
            }

            //if (File.Exists("VariableData.json"))
            //{
            //    PLCVariables = JsonConvert.DeserializeObject<BindingList<PLCVariable>>(File.ReadAllText("VariableData.json"));

            //}
            //else
            {
                //D400 冲程压边力设定 Float
                //D402 闭环压边力设定 Float
                //D412 拉伸位移上限 Float
                //D410 停机延时设定 Word
                //D416 停机比例设定 Float
                //D430 速度设定 Float


                //D364 有效拉伸位移  数据读 Float 
                //D362 最大拉伸力 数据读 Float 
                //D54  实时压边力   数据读 Float
                //D249 实时拉伸速度 数据读 Float
                //D260 实时拉伸位移  数据读 Float
                //D46 实时拉伸力 数据读 Float 
                //D66 主推力 数据读 Float

                //M10 数据重置    布尔
                //M30 冲程压边 布尔
                //M80 压边  布尔
                //M10 压边释放 布尔
                //M50 拉伸  布尔
                //M30 拉伸释放 布尔
                //M9  停止  布尔

                PLCVariables = new BindingList<PLCVariable>();
                PLCVariables.Add(new PLCVariable { Name = "冲程压边力设定", Address = "D400", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });
                PLCVariables.Add(new PLCVariable { Name = "闭环压边力设定", Address = "D402", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });
                PLCVariables.Add(new PLCVariable { Name = "速度设定", Address = "D404", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });
                PLCVariables.Add(new PLCVariable { Name = "停机延时设定", Address = "D410", DataType = "Word", CurrentValue = "0", WriteValue = "0" });
                PLCVariables.Add(new PLCVariable { Name = "拉伸位移上限", Address = "D412", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });
                PLCVariables.Add(new PLCVariable { Name = "停机比例设定", Address = "D416", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });

                PLCVariables.Add(new PLCVariable { Name = "实时拉伸力", Address = "D46", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });
                PLCVariables.Add(new PLCVariable { Name = "拉伸时间", Address = "D48", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });
                PLCVariables.Add(new PLCVariable { Name = "实时压边力", Address = "D54", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });
                PLCVariables.Add(new PLCVariable { Name = "主推力", Address = "D66", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });
                PLCVariables.Add(new PLCVariable { Name = "实时拉伸速度", Address = "D249", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });
                PLCVariables.Add(new PLCVariable { Name = "实时拉伸位移", Address = "D260", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });
                PLCVariables.Add(new PLCVariable { Name = "最大拉伸力", Address = "D362", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });
                PLCVariables.Add(new PLCVariable { Name = "有效拉伸位移", Address = "D364", DataType = "Float", CurrentValue = "0.000", WriteValue = "0.000" });


                PLCVariables.Add(new PLCVariable { Name = "压边释放", Address = "M1", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "拉伸", Address = "M50", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "拉伸释放", Address = "M3", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "停止", Address = "M9", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "数据重置", Address = "M10", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "冲程压边", Address = "M30", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "数据采集标志", Address = "M37", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "传感器标零", Address = "M60", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "传感器标零状态", Address = "M61", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "弹料", Address = "M70", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "压边", Address = "M80", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "完全复位", Address = "M111", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });


                PLCVariables.Add(new PLCVariable { Name = "压边线圈", Address = "Y6", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "压边释放线圈", Address = "Y7", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "拉伸线圈", Address = "Y4", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });
                PLCVariables.Add(new PLCVariable { Name = "拉伸释放线圈", Address = "Y5", DataType = "Boolean", CurrentValue = "False", WriteValue = "False" });



            }
        }

        /// <summary>
        /// 尝试与 PLC 建立 TCP 连接。返回 true 表示成功，false 表示失败（异常会被捕获）。
        /// PLC 不在线时本方法会在 TCP 建连超时后返回失败，调用方应放到后台线程。
        /// </summary>
        public static bool TryConnect()
        {
            try
            {
                lock (PlcConnectionLock)
                {
                    plc.Connect();
                }
                logger.Info("连接PLC成功！");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "连接PLC失败！");
                return false;
            }
        }

        public static bool TryReconnect()
        {
            try
            {
                lock (PlcConnectionLock)
                {
                    plc.AbortConnection();
                    Thread.Sleep(100);
                    plc.Connect();
                }
                logger.Info("重新连接PLC成功！");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "重新连接PLC失败！");
                return false;
            }
        }

        public static long showTime = 0;

        public static void Refresh(Dispatcher dispatcher)
        {
            Task.Run(() =>
            {
                using var hrTimer = new HighResolutionTimer();
                hrTimer.SetPeriod(10);
                hrTimer.Start();

                Stopwatch stopwatch = Stopwatch.StartNew();
                DateTime nextReconnectTime = DateTime.MinValue;
                bool beginScan = false;
                DateTime beginScanTime = DateTime.Now;
                int IndexCount = 0;
                while (!DataAqc._cts.IsCancellationRequested)
                {
                    try
                    {
                        if (AutoReconnectSuspended)
                        {
                            Thread.Sleep(200);
                            continue;
                        }

                        hrTimer.WaitForTrigger();


                        //连接了再读取
                        // if (bool.Parse(plc?.ConnectState ?? "false"))
                        {
                            var mBoolValue = plc.ReadBools((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("M1").HexAddress), 80);
                            float[]? d400FValue = null;
                            ushort? d410WValue = null;
                            if (!mBoolValue[36])
                            {
                                d400FValue = plc.ReadFloats((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D400").HexAddress), 16);
                                // D410 is the first register in the D410/D411 float at index 5.
                                d410WValue = (ushort)(BitConverter.SingleToInt32Bits(d400FValue[5]) & 0xFFFF);
                            }

                            var d46FValue = plc.ReadFloats((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D46").HexAddress), 11);
                            var d249FValue = plc.ReadFloat((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D249").HexAddress));
                            var d260FValue = plc.ReadFloat((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D260").HexAddress));
                            float[]? d362FValue = null;
                            if (!mBoolValue[36])
                            {
                                d362FValue = plc.ReadFloats((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D362").HexAddress), 2);
                            }

                            bool fullResetValue = TryReadFullResetValue();
                            var y4Value = plc.ReadBools((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("Y4").HexAddress), 4);

                            AcquisitionQueue.Enqueue(new AcquisitionBatchItem(
                                new PlcSnapshot(
                                    mBoolValue,
                                    d400FValue,
                                    d410WValue,
                                    d46FValue,
                                    d249FValue,
                                    d260FValue,
                                    d362FValue,
                                    fullResetValue,
                                    y4Value),
                                null,
                                false,
                                false));

                            if (mBoolValue[36] && !beginScan)
                            {
                                beginScan = true;
                                beginScanTime = DateTime.Now;
                                IndexCount = 0;
                                AcquisitionQueue.Enqueue(new AcquisitionBatchItem(null, null, true, false));
                            }

                            if (beginScan && (DateTime.Now - beginScanTime).TotalMinutes <= 5)
                            {
                                AcquisitionQueue.Enqueue(new AcquisitionBatchItem(
                                    null,
                                    new Loadmodel
                                    {
                                        Index = ++IndexCount,
                                        RealPress = d46FValue[4],
                                        RealDistance = d260FValue,
                                        RealForce = d46FValue[0],
                                        Time = d46FValue[1].ToString("F3")
                                    },
                                    false,
                                    false));
                            }

                            if (beginScan && !mBoolValue[36])
                            {
                                beginScan = false;
                                AcquisitionQueue.Enqueue(new AcquisitionBatchItem(null, null, false, true));
                            }

                        }

                    }
                    catch (Exception ex)
                    {
                        if (AutoReconnectSuspended)
                        {
                            continue;
                        }

                        // A failed Modbus request is the only connection-health signal.
                        // Reconnect on the first failed acquisition cycle.
                        if (DateTime.UtcNow >= nextReconnectTime)
                        {
                            nextReconnectTime = DateTime.UtcNow.AddMilliseconds(500);
                            logger.Warn(ex, "PLC 采集失败，立即断开并重连。");
                            TryReconnect();
                        }
                        else
                        {
                            Thread.Sleep(10);
                        }
                    }

                }
            });
        }



        private static bool TryReadFullResetValue()
        {
            return plc.ReadBool((ushort)ModbusAddressHelper.ConvertToModbusAddresss("M111").HexAddress);
        }

        /// <summary>
        /// 新的model 追加到队列尾部
        /// </summary>
        /// <param name="loadmodel"></param>
        public static void StartConsumers(Dispatcher dispatcher)
        {
            Task.Run(async () =>
            {
                PeriodicTimer timer = new(TimeSpan.FromMilliseconds(50));
                try
                {
                    while (await timer.WaitForNextTickAsync(_cts.Token))
                    {
                        DrainAcquisitionQueue(dispatcher);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    timer.Dispose();
                }
            }, _cts.Token);
        }

        private static void DrainAcquisitionQueue(Dispatcher dispatcher)
        {
            List<AcquisitionBatchItem> batch = [];
            while (AcquisitionQueue.TryDequeue(out AcquisitionBatchItem? item))
            {
                batch.Add(item);
            }

            if (batch.Count == 0)
            {
                return;
            }

            foreach (AcquisitionBatchItem item in batch)
            {
                if (item.Started)
                {
                    TrialDataStore.BeginNewCapture();
                }

                if (item.LoadPoint is not null)
                {
                    TrialDataStore.EnqueuePoint(SNModel.GetSn(), item.LoadPoint);
                }
            }

            List<Loadmodel> points = batch.Where(item => item.LoadPoint is not null).Select(item => item.LoadPoint!).ToList();

            dispatcher.BeginInvoke(() => ApplyBatchOnUiThread(batch, points));
        }

        private static void ApplyBatchOnUiThread(IReadOnlyList<AcquisitionBatchItem> batch, IReadOnlyList<Loadmodel> points)
        {
            foreach (AcquisitionBatchItem item in batch)
            {
                if (item.Started)
                {
                    loadModels.Clear();
                    ChartCleared?.Invoke();
                }

                if (item.Ended)
                {
                    DataCollectionEnded?.Invoke();
                }
            }

            PlcSnapshot? latestSnapshot = batch.LastOrDefault(item => item.Snapshot is not null)?.Snapshot;
            if (latestSnapshot is not null)
            {
                ApplyPlcSnapshot(latestSnapshot);
            }

            if (points.Count > 0)
            {
                loadModels.AddRange(points);
                LoadDataBatchChanged?.Invoke(points);
            }
        }

        private static void ApplyPlcSnapshot(PlcSnapshot snapshot)
        {
            if (snapshot.D400Values is not null && snapshot.D410Value.HasValue)
            {
                PLCVariables.First(t => t.Name == "冲程压边力设定").CurrentValue = snapshot.D400Values[0].ToString("F3");
                PLCVariables.First(t => t.Name == "闭环压边力设定").CurrentValue = snapshot.D400Values[1].ToString("F3");
                PLCVariables.First(t => t.Name == "速度设定").CurrentValue = snapshot.D400Values[2].ToString("F3");
                PLCVariables.First(t => t.Name == "拉伸位移上限").CurrentValue = snapshot.D400Values[6].ToString("F3");
                PLCVariables.First(t => t.Name == "停机比例设定").CurrentValue = snapshot.D400Values[8].ToString("F3");
                PLCVariables.First(t => t.Name == "停机延时设定").CurrentValue = snapshot.D410Value.Value.ToString();
            }

            PLCVariables.First(t => t.Name == "实时拉伸力").CurrentValue = snapshot.D46Values[0].ToString("F3");
            PLCVariables.First(t => t.Name == "拉伸时间").CurrentValue = snapshot.D46Values[1].ToString("F3");
            PLCVariables.First(t => t.Name == "实时压边力").CurrentValue = snapshot.D46Values[4].ToString("F3");
            PLCVariables.First(t => t.Name == "主推力").CurrentValue = snapshot.D46Values[10].ToString("F3");
            PLCVariables.First(t => t.Name == "实时拉伸速度").CurrentValue = snapshot.D249Value.ToString("F3");
            PLCVariables.First(t => t.Name == "实时拉伸位移").CurrentValue = snapshot.D260Value.ToString("F3");

            if (snapshot.D362Values is not null)
            {
                PLCVariables.First(t => t.Name == "最大拉伸力").CurrentValue = snapshot.D362Values[0].ToString("F3");
                PLCVariables.First(t => t.Name == "有效拉伸位移").CurrentValue = snapshot.D362Values[1].ToString("F3");
            }

            PLCVariables.First(t => t.Name == "压边释放").CurrentValue = snapshot.MValues[0].ToString();
            PLCVariables.First(t => t.Name == "拉伸").CurrentValue = snapshot.MValues[49].ToString();
            PLCVariables.First(t => t.Name == "拉伸释放").CurrentValue = snapshot.MValues[2].ToString();
            PLCVariables.First(t => t.Name == "停止").CurrentValue = snapshot.MValues[8].ToString();
            PLCVariables.First(t => t.Name == "数据重置").CurrentValue = snapshot.MValues[9].ToString();
            PLCVariables.First(t => t.Name == "冲程压边").CurrentValue = snapshot.MValues[29].ToString();
            PLCVariables.First(t => t.Name == "传感器标零").CurrentValue = snapshot.MValues[59].ToString();
            PLCVariables.First(t => t.Name == "传感器标零状态").CurrentValue = snapshot.MValues[60].ToString();
            PLCVariables.First(t => t.Name == "弹料").CurrentValue = snapshot.MValues[69].ToString();
            PLCVariables.First(t => t.Name == "压边").CurrentValue = snapshot.MValues[79].ToString();
            PLCVariables.First(t => t.Name == "完全复位").CurrentValue = snapshot.FullResetValue.ToString();
            PLCVariables.First(t => t.Name == "数据采集标志").CurrentValue = snapshot.MValues[36].ToString();
            PLCVariables.First(t => t.Name == "拉伸线圈").CurrentValue = snapshot.YValues[0].ToString();
            PLCVariables.First(t => t.Name == "拉伸释放线圈").CurrentValue = snapshot.YValues[1].ToString();
            PLCVariables.First(t => t.Name == "压边线圈").CurrentValue = snapshot.YValues[2].ToString();
            PLCVariables.First(t => t.Name == "压边释放线圈").CurrentValue = snapshot.YValues[3].ToString();
        }

        private sealed record AcquisitionBatchItem(PlcSnapshot? Snapshot, Loadmodel? LoadPoint, bool Started, bool Ended);
        private sealed record PlcSnapshot(bool[] MValues, float[]? D400Values, ushort? D410Value, float[] D46Values, float D249Value, float D260Value, float[]? D362Values, bool FullResetValue, bool[] YValues);



    }
}

