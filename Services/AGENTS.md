# Services 模块导航

服务负责可独立验证的业务流程；窗口只协调 UI。修改前沿下表确定所有者和不可越过的边界。

| 流程 | 文件 / 类 | 关键入口 | 约束 |
|---|---|---|---|
| 曲线 | `LoadPlotController.cs` / `LoadPlotController` | 初始化、`Refresh()`、`Reset()`、`AutoScaleWhileCollecting()` | 主窗与独立曲线窗沿现有事件链；不从调试代码手动 AutoScale |
| 网络地址探测 | `NetworkAdapterProbeService.cs` / `NetworkAdapterProbeService` | 候选有线网卡地址添加 / 回滚 | 提权进程只管理临时 IP，不连接 PLC；只移除本次新增地址；跳过 Wi-Fi |
| 摄像头 | `CameraCaptureService.cs` / `CameraCaptureService` | 设备枚举、启动、帧回调、释放 | 异步释放；主窗口只订阅帧与错误并显示 |
| 新视觉 / 二段速设备 | `NewVisionProcessingService.cs`、`VisionDeviceClient.cs`、`VisionDetectionController.cs` | 连接、检测、释放 | 与 PLC 采集分离；窗口负责提示和 UI 状态 |
| 实时 / 回放数据 | `TrialDataStore.cs` / `TrialDataStore` | 入队、试验分组、回放、临时库清理 | `DataAqc.Enqueue()` 是实时持久化接入点；调试辅助不得另写数据库 |
| 报告 | `TestReportService.cs` / `TestReportService` | 报告文档生成 | 算法整合导出不得替换报告原始曲线截图 |
| 位移重采样 | `DisplacementResamplingService.cs` / `DisplacementResamplingService` | `SaveResampledDataToFile()` | 步长=速度/20，解析失败回退 0.05mm；覆盖外侧整间隔边界并线性外推；保存前快照，后台生成 |
| 离线算法验证 | `DebugAlgorithmExcelService.cs` | 读取首表、复用重采样导出 | 仅 PIN 隐藏入口，不读写实时队列 |
| 说明书缓存 | `ManualDocumentService.cs` | 导航、格式判断、XPS 缓存 / 转换 | Word/PPT 权威缓存 `%LocalAppData%\ECS\manual-cache`；保持单文件发布路径兼容 |

## 网络探测主流程（跨层）

UI 入口和重试实现在 `../MainWindow/MainWindow.Network.cs`：`RunNetworkProbeAndReconnectAsync()`、`TryConnectWithRetriesAsync()`。探测前暂停 `DataAqc.AutoReconnectSuspended`，结束时 `finally` 恢复；主进程独占 PLC TCP 连接。提权进程、单次 `netsh` 和单次连接均须有超时；候选 IP 保持多次有限重试及约 1 秒间隔。

## 实时保存与报告

窗口入口见 `../MainWindow/AGENTS.md`。实时数据为空以 `DataAqc.loadModels` 判断，不以已排空 `_queue` 判断；保存前快照，后台生成算法整合文件；`BackgroundStartupWaitWindow` 使用独立 STA Dispatcher，不依赖 UI 同步等待。

## 本地正弦曲线调试

仅在用户明确要求时新增 `SineDebugDataService.cs`，不得作为产品功能提交。公式、重置、连接即停止和验收边界见根 `AGENTS.md`“本地调试”。调试点只能调用 `DataAqc.Enqueue()`，不得直接写曲线、`loadModels`、`TrialDataStore` 或 `LoadDataChanged`。
