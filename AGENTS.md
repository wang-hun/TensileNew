# 仓库导航（ECS）

本文件是入口索引。按“业务流程 → 子目录 `AGENTS.md` → 文件 / 类 / 函数”定位修改边界；子指南应与实现保持同步。项目是 .NET 8 WPF Windows 产品，主项目为 `TensileNeW.csproj`（程序集 `ECS`）。

## 从流程定位代码

| 流程 | 从这里开始 | 主要下游 | 详细边界 |
|---|---|---|---|
| 启动、主题、配置、PLC 首连 | `App.xaml.cs` → `App.Application_Startup()`、`TryConnectWithTimeoutAsync()` | `RAM.Init()`、`DataAqc.InitVariables()`、`StartupWaitWindow`、`MainWindow()` | `Models/AGENTS.md`、`Dialogs/AGENTS.md` |
| 主窗口交互与职责分区 | `MainWindow.xaml` 事件绑定 → `MainWindow.xaml.cs` / `MainWindow.MainWindow()` | 下列 partial、`MainViewModel`、Services | `MainWindow/AGENTS.md` |
| 主窗口帮助 / 文档 | `MainWindow.xaml` → 帮助事件 | `MainWindow/MainWindow.Help.cs` → `ManualDocumentService` → `HelpDocumentViewer` | `MainWindow/AGENTS.md`、`Services/AGENTS.md` |
| PLC / 实时采集 | `MainViewModel.cs` → PLC 写入命令；`Models/DataAqc.cs` → `InitVariables()`、`TryConnect()`、`Refresh()`、`StartConsumers()` | `Tools/DeltaPLC2.cs`、`ModbusAddressHelper.cs`、`TrialDataStore` | `Models/AGENTS.md`、`Tools/AGENTS.md` |
| 网络探测与恢复连接 | `MainWindow/MainWindow.Network.cs` → `RunNetworkProbeAndReconnectAsync()`、`TryConnectWithRetriesAsync()` | `NetworkAdapterProbeService`、`DataAqc.AutoReconnectSuspended` | `MainWindow/AGENTS.md`、`Services/AGENTS.md` |
| 曲线 / 实时表格 / 回放 | `MainWindow.xaml.cs` → `InitializePlot()`、`LoadItems_ListChanged()`、`RenderPlayback()` | `LoadPlotController`、`TrialDataStore`、`PlotWindow` | `MainWindow/AGENTS.md`、`Services/AGENTS.md`、`Dialogs/AGENTS.md` |
| 摄像头 | `MainWindow.xaml.cs` → `InitializeCameraPreview()`、`ApplySelectedCameraAsync()` | `CameraCaptureService`、选择 / 预览窗口 | `MainWindow/AGENTS.md`、`Services/AGENTS.md`、`Dialogs/AGENTS.md` |
| 试验控制 / 二段速 / 复位 | `MainWindow.xaml.cs` → `StartTensile_Click()`、`Reset_Click()`、`SwitchToSecondStageAsync()` | `MainViewModel`、`DataAqc`、视觉服务 | `MainWindow/AGENTS.md`、`Models/AGENTS.md` |
| 实时数据、算法整合、报告与回放导出 | `MainWindow.xaml.cs` → `SaveDataAndReportAsync()`、`SavePlaybackData_Click()` | `DisplacementResamplingService`、`TrialDataStore`、`TestReportService` | `MainWindow/AGENTS.md`、`Services/AGENTS.md` |
| 配置 / 配方 | `MainViewModel.cs`、`Models/RAM.cs`、`SettingModel.cs`、`RecipeModel.cs` | Settings / recipe dialogs | `Models/AGENTS.md`、`Dialogs/AGENTS.md` |
| PIN 隐藏入口 | `MainWindow.xaml.cs` → `LogoImage_MouseLeftButtonDown()` | `SettingsPinDialog` / `SettingsPinWindow`、离线导入服务 | `Dialogs/AGENTS.md`、`Services/AGENTS.md` |
| 产品发布 / 绿色包 | `builder/Builder` | 主项目 publish、启动脚本、内容断言 | `builder/AGENTS.md` |
| 安装器 | `installer/InstallerBuilder` → `installer/Installer` | 临时 payload、独立部署 EXE | `installer/AGENTS.md` |
| 独立示例 | `demo/Demo.csproj` | demo 自有页面、服务和资源 | `demo/AGENTS.md`；不属于产品 |

## 主窗口拆分图

`MainWindow.xaml.cs` 是 UI 协调中心，不是业务服务。当前文件保留窗口装配、生命周期、摄像头、曲线、试验控制、二段速、回放和保存；已按高内聚事件域拆出：

- `MainWindow/MainWindow.Help.cs`：说明书导航、预览、搜索、缩放。
- `MainWindow/MainWindow.Network.cs`：页面切换、DACU 入口、网络探测 / 重连 UI 协调。
- `MainWindow/MainWindow.InputValidation.cs`：配方数值输入规范化和校验显示。
- 具体函数索引及允许的跨层依赖见 `MainWindow/AGENTS.md`。

partial 仍共享 `MainWindow` 字段，不等于完成服务级解耦。后续应把有独立状态 / 生命周期的逻辑迁至 `Services/` 或 `MainViewModel`，并通过明确输入输出、事件或接口协作；不要再制造对整个窗口控件树的依赖。

## PLC 与实时数据：不可破坏的契约

- 当前链路：`RAM.SettingModel.PLC_IP` → `DataAqc.InitVariables()` → `DeltaPLC2`（NModbus）→ `DataAqc.Refresh()` / 消费者 → UI。端口 502、Unit ID 1；地址由 `ModbusAddressHelper` 转换。旧 `DeltaPLC.cs` 不在主路径。
- 冲程压边按下 / 松开必须后台即时写线圈。连接成功只读一次 D40（浮点版本 `2026.068`）并缓存判断：版本不低于该值只写 M45；缺失、读取失败或较低只写 M30。按键不得重读 D40；重连可重新读取一次。涉及 `DataAqc`、`MainViewModel.SetStrokeStampingAsync()` 和按键事件需一起核验。
- 采样率显示旁路接收 `UiBatchApplied` 最近 20 批全部 `PlcSnapshot` 数量，单位 tick/s。不得为此改采样周期、消费者 50ms 周期、队列消费、数据库写入、曲线或 UI 调度。
- `Refresh()` 中 M111 完全复位是设备真实链路，不能绕开。网络探测必须暂停自动重连并在 `finally` 恢复；PLC TCP 连接只由主进程持有。

## 数据导出边界

- 实时保存无表格数据时按 `DataAqc.loadModels` 是否为空直接返回，不能用已被消费者排空的 `_queue` 判断。
- 保存数据 / 报告的等待动画由独立 STA Dispatcher 的 `BackgroundStartupWaitWindow` 驱动；保存前先快照实时点，再在线程池运行算法导出。
- 算法整合文件单独命名 `{基础名}_算法整合数据.xlsx`，不能插入原始 Excel 或改变报告截图。`DisplacementResamplingService` 步长=速度设定/20（1mm/s → 0.05mm），速度解析失败才回退 0.05mm；范围外整间隔边界须线性外推。离线 PIN 导入只读所选 Excel 第一张表，并复用该服务。
- 试用保存次数、文件名及拒绝第 101 次规则维护在 package / 保存入口；权限状态不得用于引入其他限制。定位 `MainWindow.xaml.cs` 保存入口和试用状态服务，再读 `Services/AGENTS.md`。

## 本地调试功能（仅明确要求时）

正弦生成器是临时本地调试辅助，不应成为产品功能，也不得提交。参考实现：`git show d032605`（仅参考，不合并）。触发仅限 PIN 五位码 `SINON` 启动、`SINOF` 停止；不得新增按钮或持久化开关。

- 服务放 `Services/SineDebugDataService.cs`，构造传入主窗口 Dispatcher，使用约 50ms `DispatcherTimer`；已连接时拒绝启动，运行中一旦连接立即停止。只在未连接设备时工作。
- 点公式 `force = 10 * (Math.Sin(x / 2) + offset)`，初始 offset=1；每次调试专用复位 offset 加 2，x/index 归零并重启计时。力、位移、压边力、时间按原调试契约填充。
- 数据必须经 `DataAqc.Enqueue()`，采集标志通过 PLC 变量 `数据采集标志` 设 True/False，让既有自动缩放条件自然成立；不手动调用 AutoScale、不直接操作控制器 / `loadModels` / `TrialDataStore` / `LoadDataChanged`。
- 唯一允许给 `DataAqc` 增加的是调试专用 `ClearDebugLoadData()`：只清 `_queue`、`loadModels` 并触发 `ChartCleared`。`MainWindow.Reset_Click()` 调试运行时只走此入口并推进试验序列号，常规重置保持原样。
- PIN 事件透传经过 `SettingsPinDialog`、`SettingsPinWindow`；主窗只显示启动 / 停止 / 已运行 / 已连接跳过 / 未运行提示，不因调试入口操作 PLC。
- 具体文件级复现与验收还需对照 `Models/AGENTS.md`、`Services/AGENTS.md`、`Dialogs/AGENTS.md`；若实施，明确告知工作区有未提交调试改动。

## 工程隔离、编码与验证

- 产品只构建 `TensileNeW.csproj`；`demo/`、`SamplingCapture/`、builder、installer 均为独立工程 / 工具。不得将其编入产品发布包。修改 demo 另读 `demo/AGENTS.md`。
- 发布必须是 win-x64 Release、自包含单文件；根部 DLL 数量为 0，EXE、`NLog.config`、`start-{AssemblyName}.cmd`、`Assets/`、`manuals/` 必须存在。builder 输出根必须位于仓库外。详见 `builder/AGENTS.md`。
- InstallerBuilder 负责控制台选择和临时 payload；最终交付物仅是其运行目录 `publish/ECS-Installer.exe`。详见 `installer/AGENTS.md`。
- 新增 / 修改文件使用 UTF-8。单文件发布路径定位使用 `Environment.ProcessPath` 或 `AppContext.BaseDirectory`，不要依赖 `Assembly.Location`。
- 修改窗口 UI 前核算所在容器实际宽高、Grid 行列、Margin / Padding；不通过扩窗、压字体或裁切字段掩盖布局问题。复用本地按钮样式，不加装饰性边框。
- 主项目验证：`dotnet build .\TensileNeW.csproj`。独立工程分别验证；不要提交构建、运行数据库、日志、缓存或临时部署产物。
