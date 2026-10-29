# MainWindow 模块导航

本目录包含 `MainWindow` 的 partial 实现。所有 partial 仍属于同一个 WPF 窗口并共享状态，不代表已解耦成独立服务；新增业务逻辑前优先判断是否可放入 `MainViewModel` 或 `Services/`，不要继续把不相关职责塞进窗口。

## 流程与定位

| 流程 | 文件 | 类 / 函数 | 依赖与边界 |
|---|---|---|---|
| 窗口装配、启动、关闭 | `../MainWindow.xaml.cs` | `MainWindow()`、`Window_Loaded()`、`Window_Closing()`、`RunShutdownCleanup()` | 窗口协调入口；启动前置检查在 `../App.xaml.cs` 的 `Application_Startup()` |
| 窗口外观 | `../MainWindow.xaml.cs` | `OnSourceInitialized()`、`ConfigureWindowChrome()` | DPI / Win32 chrome；保持自包含发布路径兼容 |
| PLC 状态与启动配方 | `../MainWindow.xaml.cs` | `Plc_PropertyChanged()`、`WaitForPlcConnectionAsync()`、`IsPlcConnected()`、`WriteStartupRecipeAsync()` | 通过 `MainViewModel` 和 `DataAqc`，不要另建 PLC 客户端 |
| 说明书 / 帮助 | `MainWindow.Help.cs` | `LoadHelpDocument()`、`NavigateHelpItemAsync()`、`OpenManualDocumentAsync()`、`HelpSearch*()` | `ManualDocumentService`、`HelpDocumentViewer`；缓存规则见 `../Services/AGENTS.md` |
| 网络探测 / 重连 | `MainWindow.Network.cs` | `RunNetworkProbeAndReconnectAsync()`、`TryConnectWithRetriesAsync()`、`ProbeNetwork*()` | PLC TCP 连接必须由主进程持有；重试、超时和自动重连暂停见 `../Services/AGENTS.md` |
| 数值输入校验 | `MainWindow.InputValidation.cs` | `DecimalBox_*()`、`ShutdownRatioBox_*()`、`TryNormalizeDecimalInput()` | 只负责 UI 规范化和错误样式，不在此处写 PLC |
| 曲线和表格 | `../MainWindow.xaml.cs` | `InitializePlot()`、`ResetPlot()`、`RefreshPlot()`、`OpenPlotWindow()`、`LoadItems_ListChanged()`、`ScrollMainLoadDataToLatest()` | `LoadPlotController` 是绘图状态所有者；禁止主窗体绕过控制器管理曲线系列 |
| 摄像头预览 | `../MainWindow.xaml.cs` | `InitializeCameraPreview()`、`InitializeCameraAfterMainWindowShownAsync()`、`ApplySelectedCameraAsync()`、`ReleaseCameraInBackground()` | 生命周期由 `CameraCaptureService` 管理；详见 `../Services/AGENTS.md` |
| 试验控制 / 复位 | `../MainWindow.xaml.cs` | `StartTensile_Click()`、`Stop_Click()`、`Reset_Click()`、`PerformDataResetAsync()`、`OnTrialDataCleared()` | PLC 命令交给 `MainViewModel`；二段速状态转换保持在窗口协调层 |
| 二段速 | `../MainWindow.xaml.cs` | `TwoStageSpeed_Click()`、`SwitchToSecondStageAsync()`、`SwitchToFirstStageAsync()`、`ShowTwoStagePrompt()`、`RestoreTwoStageAutoResetSetting()` | 检查自动复位、序列号和采集结束自动保存交互 |
| 配方与设置 | `../MainWindow.xaml.cs` | `WriteRecipe_Click()`、`AddRecipe_Click()`、`DeleteRecipe_Click()`、`SaveSettings_Click()` | 写入和持久化归 `MainViewModel` / `RAM` |
| 实时数据保存和报告 | `../MainWindow.xaml.cs` | `SaveDataAndReportAsync()`、`OnDataCollectionEnded()`、`RunAutomaticSaveAsync()`、`SaveTestReportToFile()` | 快照、后台等待窗、试用次数、算法整合和报告约束见根索引及 `../Services/AGENTS.md` |
| 回放 | `../MainWindow.xaml.cs` | `RefreshPlaybackTrialsAsync()`、`RenderPlayback()`、`SavePlaybackData_Click()`、`SavePlaybackReport_Click()` | 读取 `TrialDataStore`；回放与实时采集不得混用状态 |
| 隐藏入口 / 调试导入 | `../MainWindow.xaml.cs` | `LogoImage_MouseLeftButtonDown()`、`RunDebugAlgorithmDataImportAsync()` | PIN 事件边界见 `../Dialogs/AGENTS.md`；算法规则见根索引 |

## 结构改动规则

- `MainWindow.xaml` 的事件名必须仍能在任意 `MainWindow` partial 中找到；移动事件处理器时核验 XAML 绑定。
- partial 之间共享字段会形成隐式耦合。只有同一 UI 生命周期 / 状态域才放在同一个 partial；跨域协作优先提取 `Services/` 服务并通过参数、事件或接口连接。
- 不要把 PLC 通信、采样循环、持久化算法复制进 code-behind。责任分别属于 `MainViewModel`、`DataAqc`、`TrialDataStore` 和对应服务。
- 窄范围验证：`dotnet build .\TensileNeW.csproj`。网络、试验控制、采集结束与自动保存变更逐项核对对应模块约束。
