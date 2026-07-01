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

        public static ConcurrentQueue<Loadmodel> _queue = new ConcurrentQueue<Loadmodel>();
        public static CancellationTokenSource _cts = new CancellationTokenSource();
        public static BindingList<Loadmodel> loadModels = new BindingList<Loadmodel>();
        private static readonly object PlcConnectionLock = new();

        public static event Action<Loadmodel> LoadDataChanged;
        public static event Action ChartCleared;
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

        public static bool TryReconnect(bool forceReconnect = false)
        {
            try
            {
                lock (PlcConnectionLock)
                {
                    if (!forceReconnect && IsPlcConnected())
                    {
                        logger.Info("PLC已连接，跳过自动重连。");
                        return true;
                    }

                    plc.Disconnect();
                    Thread.Sleep(500);
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

        private static bool IsPlcConnected()
        {
            return plc?.Client is { Connected: true }
                && bool.TryParse(plc.ConnectState, out bool connected)
                && connected;
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
                DateTime successTime = DateTime.Now;
                DateTime exceptionTime = DateTime.Now;
                bool beginScan = false;
                bool dataResetFlag = false;
                bool fullResetFlag = false;
                DateTime beginScanTime = DateTime.Now;
                DateTime beginEnqueTime = DateTime.Now;
                int IndexCount = 0;
                float temp = 0;
                while (!DataAqc._cts.IsCancellationRequested)
                {
                    try
                    {
                        if (AutoReconnectSuspended)
                        {
                            Thread.Sleep(200);
                            continue;
                        }

                        if (null == plc.Client || plc.Client.Connected == false)
                        {

                            throw new Exception("连接中断");
                        }

                        hrTimer.WaitForTrigger();


                        //连接了再读取
                        // if (bool.Parse(plc?.ConnectState ?? "false"))
                        {
                            var d400FValue = plc.ReadFloats((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D400").HexAddress), 16);
                            var d410WValue = plc.ReadUShort((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D410").HexAddress));
                            var d46FValue = plc.ReadFloats((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D46").HexAddress), 11);
                            var d249FValue = plc.ReadFloat((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D249").HexAddress));
                            var d260FValue = plc.ReadFloat((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D260").HexAddress));
                            var d362FValue = plc.ReadFloats((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D362").HexAddress), 2);
                            var mBoolValue = plc.ReadBools((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("M1").HexAddress), 80);
                            bool fullResetValue = TryReadFullResetValue();
                            var y4Value = plc.ReadBools((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("Y4").HexAddress), 4);

                            dispatcher.Invoke(() =>
                            {
                                PLCVariables.First(t => t.Name == "冲程压边力设定").CurrentValue = $"{d400FValue[0].ToString("F3")}";
                                PLCVariables.First(t => t.Name == "闭环压边力设定").CurrentValue = $"{d400FValue[1].ToString("F3")}";
                                PLCVariables.First(t => t.Name == "速度设定").CurrentValue = $"{d400FValue[2].ToString("F3")}";
                                PLCVariables.First(t => t.Name == "拉伸位移上限").CurrentValue = $"{d400FValue[6].ToString("F3")}";
                                PLCVariables.First(t => t.Name == "停机比例设定").CurrentValue = $"{d400FValue[8].ToString("F3")}";

                                PLCVariables.First(t => t.Name == "停机延时设定").CurrentValue = $"{d410WValue}";

                                PLCVariables.First(t => t.Name == "实时拉伸力").CurrentValue = d46FValue[0].ToString("F3");
                                PLCVariables.First(t => t.Name == "拉伸时间").CurrentValue = d46FValue[1].ToString("F3");
                                PLCVariables.First(t => t.Name == "实时压边力").CurrentValue = d46FValue[4].ToString("F3");
                                PLCVariables.First(t => t.Name == "主推力").CurrentValue = d46FValue[10].ToString("F3");

                                PLCVariables.First(t => t.Name == "实时拉伸速度").CurrentValue = d249FValue.ToString("F3");


                                PLCVariables.First(t => t.Name == "实时拉伸位移").CurrentValue = d260FValue.ToString("F3");


                                PLCVariables.First(t => t.Name == "最大拉伸力").CurrentValue = d362FValue[0].ToString("F3");
                                PLCVariables.First(t => t.Name == "有效拉伸位移").CurrentValue = d362FValue[1].ToString("F3");

                                PLCVariables.First(t => t.Name == "压边释放").CurrentValue = mBoolValue[0].ToString();
                                PLCVariables.First(t => t.Name == "拉伸").CurrentValue = mBoolValue[49].ToString();
                                PLCVariables.First(t => t.Name == "拉伸释放").CurrentValue = mBoolValue[2].ToString();
                                PLCVariables.First(t => t.Name == "停止").CurrentValue = mBoolValue[8].ToString();
                                PLCVariables.First(t => t.Name == "数据重置").CurrentValue = mBoolValue[9].ToString();
                                PLCVariables.First(t => t.Name == "冲程压边").CurrentValue = mBoolValue[29].ToString();
                                PLCVariables.First(t => t.Name == "传感器标零").CurrentValue = mBoolValue[59].ToString();
                                PLCVariables.First(t => t.Name == "传感器标零状态").CurrentValue = mBoolValue[60].ToString();
                                PLCVariables.First(t => t.Name == "弹料").CurrentValue = mBoolValue[69].ToString();
                                PLCVariables.First(t => t.Name == "压边").CurrentValue = mBoolValue[79].ToString();
                                PLCVariables.First(t => t.Name == "完全复位").CurrentValue = fullResetValue.ToString();
                                PLCVariables.First(t => t.Name == "数据采集标志").CurrentValue = mBoolValue[36].ToString();

                                PLCVariables.First(t => t.Name == "拉伸线圈").CurrentValue = y4Value[0].ToString();
                                PLCVariables.First(t => t.Name == "拉伸释放线圈").CurrentValue = y4Value[1].ToString();
                                PLCVariables.First(t => t.Name == "压边线圈").CurrentValue = y4Value[2].ToString();
                                PLCVariables.First(t => t.Name == "压边释放线圈").CurrentValue = y4Value[3].ToString();

                                #region 数据重置触发
                                bool dataResetTriggered = mBoolValue[9] && dataResetFlag == false;
                                bool fullResetTriggered = fullResetValue && fullResetFlag == false;
                                if (dataResetTriggered || fullResetTriggered)
                                {
                                    if (dataResetTriggered)
                                    {
                                        dataResetFlag = true;
                                    }

                                    if (fullResetTriggered)
                                    {
                                        fullResetFlag = true;
                                    }

                                    ClearQueue();
                                    IndexCount = 0;
                                    loadModels?.Clear();
                                    ChartCleared?.Invoke();
                                }

                                if (dataResetFlag && mBoolValue[9] == false)
                                {
                                    dataResetFlag = false;
                                }

                                if (fullResetFlag && fullResetValue == false)
                                {
                                    fullResetFlag = false;
                                }
                                #endregion


                                if (mBoolValue[36] && beginScan == false)
                                {
                                    temp = 0;
                                    beginScan = true;
                                    beginScanTime = DateTime.Now;
                                    IndexCount = loadModels?.Count ?? 0;
                                }

                                if (beginScan && (DateTime.Now - beginScanTime).TotalMinutes <= 5)//&&(DateTime.Now-beginEnqueTime).TotalMilliseconds>20)
                                {
                                    beginEnqueTime = DateTime.Now;
                                    //超时5分钟
                                    Enqueue(new Loadmodel()
                                    {
                                        Index = ++IndexCount,
                                        RealPress = d46FValue[4],
                                        RealDistance = d260FValue,
                                        RealForce = d46FValue[0],
                                        Time = d46FValue[1].ToString("F3") //beginEnqueTime.ToString("HH:mm:ss.fff")

                                    });
                                }

                                if (beginScan && mBoolValue[36] == false)
                                {
                                    beginScan = false;
                                }


                            });

                            //本次成功时间
                            successTime = DateTime.Now;

                            //stopwatch.Stop();
                            //showTime = stopwatch.ElapsedMilliseconds;
                            //Debug.WriteLine(showTime);
                            //stopwatch.Restart();

                        }

                    }
                    catch (Exception ex)
                    {
                        Thread.Sleep(500);
                        if (AutoReconnectSuspended)
                        {
                            continue;
                        }
                        //上次成功时间 过了5秒了
                        if ((DateTime.Now - exceptionTime).TotalSeconds > 5)
                        {
                            exceptionTime = DateTime.Now;
                            logger.Error("超时，自动断开重连！");
                            TryReconnect();
                        }
                    }

                }
            });
        }



        private static bool TryReadFullResetValue()
        {
            try
            {
                return plc.ReadBool((ushort)ModbusAddressHelper.ConvertToModbusAddresss("M111").HexAddress);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "读取完全复位 M111 失败，按未触发处理。");
                return false;
            }
        }

        /// <summary>
        /// 新的model 追加到队列尾部
        /// </summary>
        /// <param name="loadmodel"></param>
        private static Exception tempEnqueueEx = null;
        public static void Enqueue(Loadmodel loadmodel)
        {
            try
            {
                try
                {
                    TrialDataStore.EnqueuePoint(SNModel.GetSn(), loadmodel);
                }
                catch (Exception ex)
                {
                    logger.Error(ex.Message);
                }

                _queue.Enqueue(loadmodel);
            }
            catch (Exception ex)
            {
                if (tempEnqueueEx != ex)
                    logger.Error(ex.Message);
                tempEnqueueEx = ex;
            }
        }

        private static void ClearQueue()
        {
            while (_queue.TryDequeue(out _))
            {
            }
        }


        /// <summary>
        /// 收到开始采集信号后--首先开启一个消费者线程
        /// </summary>
        public static void StartConsumers(Dispatcher dispatcher)//CancellationToken cancellationToken, Action<Loadmodel> consumer
        {

            Task.Run(() =>
            {
                while (!DataAqc._cts.IsCancellationRequested)
                {
                    if (DataAqc._queue.TryDequeue(out Loadmodel item))
                    {
                        try
                        {
                            dispatcher.BeginInvoke(() =>
                            {  //consumer(item);
                               //写入datagrid
                                loadModels.Add(item);
                                LoadDataChanged?.Invoke(item);
                            });

                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex.Message);
                        }
                    }
                    else
                    {
                        Thread.SpinWait(5); // 无任务时降低CPU占用
                    }
                }

            }, DataAqc._cts.Token);

        }



    }
}

