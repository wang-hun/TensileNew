using CommunityToolkit.Mvvm.ComponentModel;
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
using TensileNeW.Tools;

namespace TensileNeW.Models
{
    public class DataAqc
    {
        public static Logger logger = LogManager.GetCurrentClassLogger();


        public static BindingList<PLCVariable> PLCVariables;
        public static DeltaPLC2 plc { get; private set; }

        public static ConcurrentQueue<Loadmodel> _queue = new ConcurrentQueue<Loadmodel>();
        public static CancellationTokenSource _cts = new CancellationTokenSource();
        public static BindingList<Loadmodel> loadModels = new BindingList<Loadmodel>();

        public static event Action<Loadmodel> LoadDataChanged;
        public static event Action ChartCleared;
        public static bool _simlatueRunFlag = false;

        /// <summary>
        /// 初始化本地状态（PLC 变量表和 plc 客户端对象），不发起网络连接。
        /// 连接请在后台线程通过 <see cref="TryConnect"/> 调用。
        /// </summary>
        public static void InitVariables()
        {
            //if (File.Exists("VariableData.json"))
            //{
            //    PLCVariables = JsonConvert.DeserializeObject<BindingList<PLCVariable>>(File.ReadAllText("VariableData.json"));

            //}
            //else
            {
                //D400 冲程压边力设定 Float
                //D402 闭环压边力设定 Float
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
                //M2  拉伸  布尔
                //M30 拉伸释放 布尔
                //M9  停止  布尔

                PLCVariables = new BindingList<PLCVariable>();
                PLCVariables.Add(new PLCVariable { Name = "冲程压边力设定", Address = "D400", DataType = "Float" });
                PLCVariables.Add(new PLCVariable { Name = "闭环压边力设定", Address = "D402", DataType = "Float" });
                PLCVariables.Add(new PLCVariable { Name = "速度设定", Address = "D404", DataType = "Float" });
                PLCVariables.Add(new PLCVariable { Name = "停机延时设定", Address = "D410", DataType = "Word" });
                PLCVariables.Add(new PLCVariable { Name = "停机比例设定", Address = "D416", DataType = "Float" });

                PLCVariables.Add(new PLCVariable { Name = "实时拉伸力", Address = "D46", DataType = "Float" });
                PLCVariables.Add(new PLCVariable { Name = "拉伸时间", Address = "D48", DataType = "Float" });
                PLCVariables.Add(new PLCVariable { Name = "实时压边力", Address = "D54", DataType = "Float" });
                PLCVariables.Add(new PLCVariable { Name = "主推力", Address = "D66", DataType = "Float" });
                PLCVariables.Add(new PLCVariable { Name = "实时拉伸速度", Address = "D249", DataType = "Float" });
                PLCVariables.Add(new PLCVariable { Name = "实时拉伸位移", Address = "D260", DataType = "Float" });
                PLCVariables.Add(new PLCVariable { Name = "最大拉伸力", Address = "D362", DataType = "Float" });
                PLCVariables.Add(new PLCVariable { Name = "有效拉伸位移", Address = "D364", DataType = "Float" });


                PLCVariables.Add(new PLCVariable { Name = "压边释放", Address = "M1", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "拉伸", Address = "M2", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "拉伸释放", Address = "M3", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "停止", Address = "M9", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "数据重置", Address = "M10", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "冲程压边", Address = "M30", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "数据采集标志", Address = "M37", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "传感器标零", Address = "M60", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "传感器标零状态", Address = "M61", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "弹料", Address = "M70", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "压边", Address = "M80", DataType = "Boolean" });


                PLCVariables.Add(new PLCVariable { Name = "压边线圈", Address = "Y6", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "压边释放线圈", Address = "Y7", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "拉伸线圈", Address = "Y4", DataType = "Boolean" });
                PLCVariables.Add(new PLCVariable { Name = "拉伸释放线圈", Address = "Y5", DataType = "Boolean" });



            }

            plc = new DeltaPLC2(RAM.SettingModel.PLC_IP);
        }

        /// <summary>
        /// 尝试与 PLC 建立 TCP 连接。返回 true 表示成功，false 表示失败（异常会被捕获）。
        /// PLC 不在线时本方法会阻塞到 TCP 系统超时（约 21 秒），调用方应放到后台线程。
        /// </summary>
        public static bool TryConnect()
        {
            try
            {
                plc.Connect();
                logger.Info("连接PLC成功！");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "连接PLC失败！");
                return false;
            }
        }
        public static long showTime = 0;

        public static void Refresh(Dispatcher dispatcher)
        {
            Task.Run(async () =>
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                DateTime successTime = DateTime.Now;
                DateTime exceptionTime = DateTime.Now;
                bool beginScan = false;
                bool dataResetFlag = false;
                DateTime beginScanTime = DateTime.Now;
                DateTime beginEnqueTime = DateTime.Now;
                int IndexCount = 0;
                float temp = 0;
                while (!DataAqc._cts.IsCancellationRequested)
                {
                    try
                    {
                        if (null == plc.Client || plc.Client.Connected == false)
                        {

                            throw new Exception("连接中断");
                        }

                        await Task.Delay(10);


                        //连接了再读取
                        // if (bool.Parse(plc?.ConnectState ?? "false"))
                        {
                            dispatcher.Invoke(() =>
                            {
                                var d400FValue = plc.ReadFloats((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D400").HexAddress), 16);
                                PLCVariables.First(t => t.Name == "冲程压边力设定").CurrentValue = $"{d400FValue[0].ToString("F3")}";
                                PLCVariables.First(t => t.Name == "闭环压边力设定").CurrentValue = $"{d400FValue[1].ToString("F3")}";
                                PLCVariables.First(t => t.Name == "速度设定").CurrentValue = $"{d400FValue[2].ToString("F3")}";
                                PLCVariables.First(t => t.Name == "停机比例设定").CurrentValue = $"{d400FValue[8].ToString("F3")}";

                                var d410WValue = plc.ReadUShort((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D410").HexAddress));
                                PLCVariables.First(t => t.Name == "停机延时设定").CurrentValue = $"{d410WValue}";

                                var d46FValue = plc.ReadFloats((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D46").HexAddress), 11);
                                PLCVariables.First(t => t.Name == "实时拉伸力").CurrentValue = d46FValue[0].ToString("F3");
                                PLCVariables.First(t => t.Name == "拉伸时间").CurrentValue = d46FValue[1].ToString("F3");
                                PLCVariables.First(t => t.Name == "实时压边力").CurrentValue = d46FValue[4].ToString("F3");
                                PLCVariables.First(t => t.Name == "主推力").CurrentValue = d46FValue[10].ToString("F3");

                                var d249FValue = plc.ReadFloat((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D249").HexAddress));
                                PLCVariables.First(t => t.Name == "实时拉伸速度").CurrentValue = d249FValue.ToString("F3");


                                var d260FValue = plc.ReadFloat((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D260").HexAddress));
                                PLCVariables.First(t => t.Name == "实时拉伸位移").CurrentValue = d260FValue.ToString("F3");


                                var d362FValue = plc.ReadFloats((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("D362").HexAddress), 2);
                                PLCVariables.First(t => t.Name == "最大拉伸力").CurrentValue = d362FValue[0].ToString("F3");
                                PLCVariables.First(t => t.Name == "有效拉伸位移").CurrentValue = d362FValue[1].ToString("F3");

                                var m2bValue = plc.ReadBools((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("M1").HexAddress), 80);
                                PLCVariables.First(t => t.Name == "压边释放").CurrentValue = m2bValue[0].ToString();
                                PLCVariables.First(t => t.Name == "拉伸").CurrentValue = m2bValue[1].ToString();
                                PLCVariables.First(t => t.Name == "拉伸释放").CurrentValue = m2bValue[2].ToString();
                                PLCVariables.First(t => t.Name == "停止").CurrentValue = m2bValue[8].ToString();
                                PLCVariables.First(t => t.Name == "数据重置").CurrentValue = m2bValue[9].ToString();
                                PLCVariables.First(t => t.Name == "冲程压边").CurrentValue = m2bValue[29].ToString();
                                PLCVariables.First(t => t.Name == "传感器标零").CurrentValue = m2bValue[59].ToString();
                                PLCVariables.First(t => t.Name == "传感器标零状态").CurrentValue = m2bValue[60].ToString();
                                PLCVariables.First(t => t.Name == "弹料").CurrentValue = m2bValue[69].ToString();
                                PLCVariables.First(t => t.Name == "压边").CurrentValue = m2bValue[79].ToString();
                                PLCVariables.First(t => t.Name == "数据采集标志").CurrentValue = m2bValue[36].ToString();

                                var y4Value = plc.ReadBools((ushort)(ModbusAddressHelper.ConvertToModbusAddresss("Y4").HexAddress), 4);
                                PLCVariables.First(t => t.Name == "拉伸线圈").CurrentValue = y4Value[0].ToString();
                                PLCVariables.First(t => t.Name == "拉伸释放线圈").CurrentValue = y4Value[1].ToString();
                                PLCVariables.First(t => t.Name == "压边线圈").CurrentValue = y4Value[2].ToString();
                                PLCVariables.First(t => t.Name == "压边释放线圈").CurrentValue = y4Value[3].ToString();

                                #region 数据重置触发
                                if (m2bValue[9] && dataResetFlag == false)
                                {
                                    dataResetFlag = true;
                                    loadModels?.Clear();
                                    ChartCleared?.Invoke();
                                }

                                if (dataResetFlag && m2bValue[9] == false)
                                {
                                    dataResetFlag = false;
                                }
                                #endregion


                                if (m2bValue[36] && beginScan == false)
                                {
                                    temp = 0;
                                    beginScan = true;
                                    beginScanTime = DateTime.Now;
                                    IndexCount = 0;
                                    loadModels?.Clear();
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

                                if (beginScan && m2bValue[36] == false)
                                {
                                    beginScan = false;
                                    //Task.Run(() =>
                                    //{  //保存excel
                                    //    try
                                    //    {
                                    //        if (!Directory.Exists(RAM.SettingModel.ExcelFolderPath))
                                    //            Directory.CreateDirectory(RAM.SettingModel.ExcelFolderPath);

                                    //        string fileName = Path.Combine(RAM.SettingModel.ExcelFolderPath, $"{RAM.SettingModel.CurRecipeModel.RecipeName}_{SNModel.GetSn()}_{DateTime.Now.ToString("yyyyMMddHHmmss")}");

                                    //        stopwatch.Restart();
                                    //        using (var exporter = new ExcelExporter_EPPlus())
                                    //        {
                                    //            exporter.CreateSheet("Orders")
                                    //                .SetHeader(new[] { "序号", "压力", "位移", "载荷", "时间" })
                                    //                .AddData(DataAqc.loadModels, o => new object[] { o.Index, o.RealPress, o.RealDistance, o.RealForce, o.Time })
                                    //                .SaveToFile(fileName);

                                    //        }
                                    //        stopwatch.Stop();
                                    //        showTime = stopwatch.ElapsedMilliseconds;
                                    //        Debug.WriteLine("EPPlus:" + showTime);
                                    //    }
                                    //    catch (Exception ex)
                                    //    {

                                    //    }
                                    //});

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
                        await Task.Delay(500);
                        //上次成功时间 过了5秒了
                        if ((DateTime.Now - exceptionTime).TotalSeconds > 5)
                        {
                            exceptionTime = DateTime.Now;
                            logger.Error("超时，自动断开重连！");
                            try
                            {  //先断开 再重连
                                plc.Disconnect();
                                await Task.Delay(500);
                                plc.Connect();
                            }
                            catch (Exception ex1)
                            {
                                logger.Error(ex1.Message);
                            }
                        }
                    }

                }
            });
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
                _queue.Enqueue(loadmodel);
            }
            catch (Exception ex)
            {
                if (tempEnqueueEx != ex)
                    logger.Error(ex.Message);
                tempEnqueueEx = ex;
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
                            dispatcher.Invoke(() =>
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

