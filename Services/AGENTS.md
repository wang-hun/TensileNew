# Services 模块导航

服务负责可独立验证的业务流程；窗口只协调 UI。修改前沿下表确定所有者和不可越过的边界。

| 流程 | 文件 / 类 | 关键入口 | 约束 |
|---|---|---|---|
| 曲线 | `LoadPlotController.cs` / `LoadPlotController` | 初始化、`Refresh()`、`Reset()`、`AutoScaleWhileCollecting()` | 主窗与独立曲线窗沿现有事件链；不从调试代码手动 AutoScale |
| 网络地址探测 | `NetworkAdapterProbeService.cs` / `NetworkAdapterProbeService` | 候选有线网卡地址添加 / 回滚 | 提权进程只管理临时 IP，不连接 PLC；只移除本次新增地址；跳过 Wi-Fi |
| 摄像头预览 | `CameraCaptureService.cs` / `CameraCaptureService` | 设备枚举、启动、帧回调、释放 | 现有预览链；不得与视觉处理 SDK 相机混用 |
| 新视觉 / 二段速 / SDK 相机 | `NewVisionProcessingService.cs`、`VisionCameraContracts.cs`、`VisionCameraSdkSelection.cs`、`MvsCameraSdkService.cs`、`DahengCameraSdkService.cs` | 模块门控、相机适配器、采集与释放 | 所有 SDK 调用仅在服务专属 worker；UI 通过异步接口调用；与 PLC 控制和周期采集隔离 |
| 实时 / 回放数据 | `TrialDataStore.cs` / `TrialDataStore` | 入队、试验分组、回放、临时库清理 | `DataAqc.Enqueue()` 是实时持久化接入点；调试辅助不得另写数据库 |
| 报告 | `TestReportService.cs` / `TestReportService` | 报告文档生成 | 算法整合导出不得替换报告原始曲线截图 |
| 新视觉图片保存 | `VisionImageSaveQueue.cs` / `VisionImageSaveQueue` | 专属 worker 串行 BMP 编码与落盘 | 保存使用独立于 UI 的最新采集帧快照和有界异步队列；连续保存通过异步背压等待队列空位，不丢弃已触发图像；检测时附加覆盖固定 BMP；不在 UI 线程编码 / 写盘 |
| 识别软件通信 | `VisionSoftwareService.cs`、`VisionCrackReaction.cs` | TCP 状态机、图片回复超时、裂痕响应 | 二段速后 start，停止时 end；避免重复发送；通信失败不阻断试验；裂痕当前仅触发停止动作。TCP stop 不含检测框，不能直接驱动标注窗体 |
| 位移重采样 | `DisplacementResamplingService.cs` / `DisplacementResamplingService` | `SaveResampledDataToFile()` | 步长=速度/20，解析失败回退 0.05mm；覆盖外侧整间隔边界并线性外推；保存前快照，后台生成 |
| 离线算法验证 | `DebugAlgorithmExcelService.cs` | 读取首表、复用重采样导出 | 仅 PIN 隐藏入口，不读写实时队列 |
| 说明书缓存 | `ManualDocumentService.cs` | 导航、格式判断、XPS 缓存 / 转换 | Word/PPT 权威缓存 `%LocalAppData%\ECS\manual-cache`；保持单文件发布路径兼容 |

## 网络探测主流程（跨层）

UI 入口和重试实现在 `../MainWindow/MainWindow.Network.cs`：`RunNetworkProbeAndReconnectAsync()`、`TryConnectWithRetriesAsync()`。探测前暂停 `DataAqc.AutoReconnectSuspended`，结束时 `finally` 恢复；主进程独占 PLC TCP 连接。提权进程、单次 `netsh` 和单次连接均须有超时；候选 IP 保持多次有限重试及约 1 秒间隔。

## 实时保存与报告

窗口入口见 `../MainWindow/AGENTS.md`。实时数据为空以 `DataAqc.loadModels` 判断，不以已排空 `_queue` 判断；保存前快照，后台生成算法整合文件；`BackgroundStartupWaitWindow` 使用独立 STA Dispatcher，不依赖 UI 同步等待。

## 本地正弦曲线调试

仅在用户明确要求时新增 `SineDebugDataService.cs`，不得作为产品功能提交。公式、重置、连接即停止和验收边界见根 `AGENTS.md`“本地调试”。调试点只能调用 `DataAqc.Enqueue()`，不得直接写曲线、`loadModels`、`TrialDataStore` 或 `LoadDataChanged`。

- 新视觉 SDK 相机的 UI、上次设备选择、图片保存路径与连续保存间隔由 `VisionCameraPanel` 持久化到 `Setting.json`；仅在 `NewVisionProcessingService.IsEnabled` 时启动枚举与自动连接。`MvsCameraSdkService` 以稳定设备序列号重新解析当前索引，不能持久化即插即变的枚举索引。
- 视觉相机 worker 的实时采集失败必须记录带原始异常的 NLog Error，再通知 UI；大恒 GetImage 的等待超时只表示本次尚未收到图像，不能因超时次数自动停采；连续无帧需节流记录带原始异常的警告，其他采集异常仍记录错误并通知 UI。日志路径由根目录 NLog.config 配置。

- VisionCameraSdkResolver.Resolve() 依据构建标记和配置选择单一工厂；华睿探测目标机安装的 MVSDKmd.dll，大恒通过 GxIAPINET.dll 初始化并依赖目标机安装的厂商原生运行库，禁止自行预加载或打包原生 SDK。初始化失败时主窗仍运行并提示管理员。大恒 SDK 初始化、枚举、采集、图像转换、保存与销毁均在专属 worker；品牌选择不得改变 WinRT 摄像头预览路径。

## 大恒 SDK 实施复盘

- 先完整核对仓库中的《DotNET软件开发说明书》和所安装 SDK 的示例，再设计采集、预览及保存；不得凭接口名称猜测厂商 API 或自行替代已有 SDK 能力。WPF 显示适配须与 SDK 图像转换分清职责，不得声称控件包装是厂商预览接口。
- 严禁越权扩展、替代或绕过已提供的 `GxIAPINET.dll`：大恒相机的初始化、连接、采集、预览转换、保存和释放必须使用其官方接口。不得擅自引入、复制、加载或打包额外原生 SDK 文件；必须先核对厂商文档和调用链，不得将托管封装与其运行时依赖混为一谈，也不得以猜测依赖代替验证。
- 编译通过不等于相机保存成功。必须区分 SDK 实际保存验证、无设备模拟验证和真实相机验证；缺少设备时明确说明，不得将静态分析或 DLL 存在当成运行成功。
