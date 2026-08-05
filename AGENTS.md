# AGENTS.md

本文件是本仓库的本地协作说明，用于让后续维护者和自动化编码代理快速理解项目结构、关键约束和验证方式。它不应作为产品功能文档对外发布。

## 项目概览

这是一个基于 WPF 的 .NET 8 Windows 桌面程序，项目文件为 `TensileNeW.csproj`，程序集名为 `ECS`。程序面向拉伸/成形试验设备，核心职责包括：

- 通过 Modbus TCP 连接台达 PLC。
- 读取实时力、位移、速度、压边力、拉伸时间等 PLC 数据。
- 写入配方参数和线圈控制指令。
- 展示实时曲线、数据表格、配方管理、试验说明文档和测试报告相关界面。

主窗口和主要交互在 `MainWindow.xaml`、`MainWindow.xaml.cs` 和 `MainViewModel.cs` 中。应用启动入口在 `App.xaml.cs`。

## Demo 工程隔离

`demo/` 是与产品程序并列的独立 WPF 示例工程，项目文件为 `demo/Demo.csproj`，程序集名为 `demo`。它用于演示可扩展的设备连接、采集接口、曲线、摄像头、PDF 和导出框架，不是 `ECS` 产品程序的一部分。

- 产品构建、发布、绿色版打包和安装器只以 `TensileNeW.csproj` / `ECS` 为目标；不得把 `demo/` 编入产品项目、builder、installer 或产品发布包。
- 修改 `demo/` 时必须遵守 `demo/AGENTS.md`；其中的空采集映射、示例数据、PDF-only 阅读边界和 demo 标识不得回流到产品程序。
- 修改产品代码时不要以 demo 页面、配置或示例曲线作为产品行为依据。需要验证 demo 时单独运行 `dotnet build .\demo\Demo.csproj`，并把 demo 的 `bin/`、`obj/` 和运行数据视为独立验证产物。

## 目录职责

- `Models/`：运行状态、配置、配方、PLC 变量、试验数据等模型。`RAM.cs` 负责加载和保存 `Setting.json`，`DataAqc.cs` 负责 PLC 变量初始化、连接、采集循环和数据队列。
- `Tools/`：PLC 通信和工具类。当前实际通信类是 `DeltaPLC2.cs`，使用 `NModbus`；`DeltaPLC.cs` 是旧的手工 Modbus TCP 实现，当前不作为运行主路径。
- `Services/`：曲线控制、说明文档缓存、试验报告、试验数据存储等服务。
- `Dialogs/`：设置、配方、数据窗口、曲线窗口、启动等待窗口等 WPF 弹窗。
- `Controls/`：复用控件，例如说明文档查看器；文档预览只保留 PDF、Word、PPT 内嵌显示，不再引入 WebView/WebView2 这类内嵌浏览器依赖。
- `Themes/`：颜色方案和主题资源。
- `Assets/`：图标、Logo、默认配方和字体资源。
- `manuals/`：发布包中的试验指导文档目录，支持 PDF、Word、PPT 文档通过 XPS 预览控件内嵌显示。Word/PPT 的 XPS 权威缓存位于 `%LocalAppData%\ECS\manual-cache`；启动和按需转换时优先按说明书文件名及内容签名命中该目录，命中后复制到运行目录的 `manual-cache`，未命中才生成并写入 AppData 后复制到运行目录。
- `demo/`：独立示例工程，不参与本产品项目编译、发布或安装器打包；其维护规则见 `demo/AGENTS.md`。
- `builder/`：打包/发布辅助项目，不参与主项目编译。
- `installer/`：独立 WPF 安装器项目。安装器构建时先调用 `builder/` 生成绿色版发布目录，再把发布目录压缩并嵌入安装器 EXE；安装器运行时必须独立工作，不再依赖 builder。

## 启动流程

`App.xaml.cs` 的 `Application_Startup` 是启动主线：

1. 调用 `RAM.Init()` 读取或创建配置。
2. 应用主题和七段数码字体资源。
3. 调用 `DataAqc.InitVariables()` 初始化 PLC 变量和 `DeltaPLC2` 客户端对象。
4. 显示 `StartupWaitWindow`，准备试验指导文档缓存。
5. 调用 `TryConnectWithTimeoutAsync()`，后台执行 `DataAqc.TryConnect()` 连接 PLC。
6. 创建并显示 `MainWindow`，将启动连接结果传入窗口。

需要在启动时增加前置检查时，优先挂在 `DataAqc.TryConnect()` 之前，避免阻塞 UI 线程。

## PLC 通信

当前 PLC 通信链路：

- 配置 IP：`RAM.SettingModel.PLC_IP`，默认值在 `Models/SettingModel.cs`。
- 初始化位置：`Models/DataAqc.cs` 的 `DataAqc.InitVariables()`。
- 通信类：`Tools/DeltaPLC2.cs`。
- 协议：Modbus TCP。
- 默认端口：502。
- 默认 Unit ID / Slave ID：1。
- 地址转换：`Tools/ModbusAddressHelper.cs`。

`DataAqc.Refresh()` 会循环读取 PLC 数据并更新 `PLCVariables`、采集队列和曲线数据。PLC 连接、重连和采集循环涉及后台线程，修改时要注意线程安全、UI Dispatcher 调用和连接状态判断。

## 本地正弦曲线调试

用户可能会临时要求恢复正弦曲线调试功能，用于在没有连接设备时验证曲线图、数据表格和 `TrialDataStore` 的真实数据链路。这个功能是本地调试辅助，不应作为产品功能保存并提交；实现后必须明确告知当前工作区存在未提交调试改动。

一比一参考实现保存在分支 `debug/sine-generator-reference` 的提交 `d032605`（`保存无设备正弦调试实现参考`）。后续需要复现时，优先查看这个提交的 diff：`git show d032605`。该提交只作为实现参考，不应合并或提交到 `master` 作为产品功能。

硬性边界：

- 只允许在设备未连接时生成调试数据。只要 `DataAqc.plc?.Client.Connected == true` 且 `DataAqc.plc.ConnectState` 能解析为 `true`，启动时必须跳过，运行中必须立即停止。
- 不允许修改 `DataAqc.Refresh()`、`DeltaPLC2`、PLC 连接、重连、自动重连、采集周期或 PLC 读写逻辑。唯一允许在 `DataAqc` 增加的是不进入采集循环的公共调试清空入口，用于复用现有 `ChartCleared` 事件。
- 不允许在采集循环里增加调试判断，以免影响真实采集周期精度。
- `DataAqc.Refresh()` 中现有 `M111`“完全复位”读取和触发逻辑属于真实设备重置链路，会与 `M10`“数据重置”一样清空队列、清空 `loadModels` 并触发 `ChartCleared`。恢复或调整正弦曲线调试时不要删除、绕过或改写这条 M111 逻辑；调试功能只在主窗口“数据重置”按钮路径上额外拦截运行中的本地调试重置。
- 不允许直接改曲线控制器、直接写 `DataAqc.loadModels`、直接触发 `LoadDataChanged` 或直接写 `TrialDataStore`。
- 调试数据必须走 `DataAqc.Enqueue(loadModel)`。这样会自然进入 `TrialDataStore.EnqueuePoint()`、消费者队列、主窗口数据表格、主窗口曲线和独立曲线窗口。
- 自动播放曲线图不能通过手动调用 `AutoScale()` 实现。调试功能必须在启动时把 PLC 变量表中的 `数据采集标志` 设为 `True`，停止时设为 `False`，让 `LoadPlotController.AutoScaleWhileCollecting()` 的现有判断自然通过。
- 数据重置不能直接操作曲线控制器。调试运行时点击主窗口“数据重置”，必须只走调试专用清空入口触发 `DataAqc.ChartCleared`，让主窗口曲线和独立曲线窗口沿用现有重置事件链。
- 不写入 `Setting.json`，不持久化开关状态，不随程序自动启动。

固定触发方式：

- 管理员密码窗口仍保持五位输入。
- `SINON` 启动正弦调试数据。
- `SINOF` 停止正弦调试数据。注意是五位 `SINOF`，不是 `SINOFF`。
- 触发入口是 `SettingsPinDialog` / `SettingsPinWindow`，不要新增公开按钮或菜单。

文件级复现步骤：

1. 新增 `Services/SineDebugDataService.cs`。
2. 在服务里定义 `SineDebugDataResult`，至少包含 `Started`、`Stopped`、`AlreadyRunning`、`NotRunning`、`SkippedBecauseConnected`。
3. `SineDebugDataService` 构造函数接收主窗口 `Dispatcher`，内部用 `DispatcherTimer` 周期生成数据。推荐周期 `50ms`。
4. 服务公开 `Start()` 和 `Stop()`。`Start()` 先检查 PLC 连接，已连接则返回 `SkippedBecauseConnected`；已运行则返回 `AlreadyRunning`；否则启动定时器。`Stop()` 停止定时器。
5. `Start()` 启动定时器前必须调用服务内部 `SetDataCollectingFlag(true)`，把 `DataAqc.PLCVariables` 中名称为 `数据采集标志` 的 `CurrentValue` 设为 `True`。`Stop()` / `StopTimer()` 必须调用 `SetDataCollectingFlag(false)`。
6. 定时器 Tick 内再次检查 PLC 连接，若已连接，立即停止并返回，不再生成点。
7. 服务内部保留 `_formulaOffset`，初始值为 `1`。Tick 内生成：
   - `force = 10 * (Math.Sin(x / 2) + _formulaOffset)`
   - `Loadmodel.RealForce = (float)force`
   - `Loadmodel.RealDistance = (float)x`
   - `Loadmodel.Index` 从当前 `DataAqc.loadModels.Count` 后继续递增
   - `Loadmodel.RealPress = 0`
   - `Loadmodel.Time` 可使用调试计时秒数 `Stopwatch.Elapsed.TotalSeconds.ToString("F3")`
8. Tick 末尾只调用 `DataAqc.Enqueue(loadModel)`，不要调用其他绘图或存储接口。
9. 在 `Models/DataAqc.cs` 增加公共方法 `ClearDebugLoadData()`，方法内容只能是：清空 `_queue`、清空 `loadModels`、触发 `ChartCleared?.Invoke()`。不要在 `Refresh()` 循环中加入任何调试判断。
10. 在 `SineDebugDataService` 增加 `ResetIfRunning()`：未运行返回 `false`；运行中调用 `DataAqc.ClearDebugLoadData()`，把 `_index = 0`、`_x = 0`、`_formulaOffset += 2`，重启计时，并保持 `数据采集标志=True`，然后返回 `true`。
11. 调试重置后的函数必须从 `10 * (Math.Sin(x / 2) + 1)` 变为 `10 * (Math.Sin(x / 2) + 3)`；之后每次重置继续把常量加 2，即 `+5`、`+7`，以此类推。
12. 在 `Dialogs/SettingsPinDialog.xaml.cs` 增加常量 `SINON`、`SINOF`，增加 `SineDebugStartRequested`、`SineDebugStopRequested` 事件，识别密码后触发事件并关闭弹窗。
13. 在 `Dialogs/SettingsPinWindow.xaml.cs` 透传 `SineDebugStartRequested`、`SineDebugStopRequested`。
14. 在 `MainWindow.xaml.cs` 增加字段 `_sineDebugDataService`，构造函数 `InitializeComponent()` 后实例化：`new SineDebugDataService(Dispatcher)`。
15. 在 `LogoImage_MouseLeftButtonDown` 创建 `SettingsPinWindow` 后订阅启动/停止事件，分别调用主窗口私有方法处理结果。
16. 主窗口处理启动/停止结果时只显示 Growl 提示，不做 PLC 操作：启动成功、已运行、设备已连接跳过、停止成功、未运行。
17. 在 `MainWindow.Reset_Click` 开头增加特殊条件：如果 `_sineDebugDataService.ResetIfRunning()` 返回 `true`，只调用 `_viewModel.AdvanceTrialSerialNumber()` 后 `return`；不要调用 `_viewModel.PulseAsync("数据重置")`。如果返回 `false`，保留原始重置逻辑不变。

验收检查：

- `SINON` 在未连接设备时，主数据表格持续新增点，曲线图跟随现有 `DataAqc.LoadDataChanged` 刷新，独立曲线窗口也能刷新。
- `SINON` 启动后，现有“自动播放曲线图”必须可工作；检查点是 `数据采集标志=True` 后 `LoadPlotController.AutoScaleWhileCollecting()` 的原始条件成立，而不是新增手动 `AutoScale()` 调用。
- 调试运行时点击“数据重置”，主窗口曲线和独立曲线窗口都必须收到 `ChartCleared` 并重置；后续新数据必须让 `LoadPlotController` 创建一条新颜色曲线。
- 第一次调试曲线公式为 `10 * (Math.Sin(x / 2) + 1)`；第一次重置后新曲线公式为 `10 * (Math.Sin(x / 2) + 3)`；再重置后为 `+5`，以后每次重置常量继续 `+2`。
- 数据库写入路径来自 `DataAqc.Enqueue()` 内部的 `TrialDataStore.EnqueuePoint()`，不应存在额外写库调用。
- 连接设备后再次输入 `SINON` 应提示跳过；调试生成过程中一旦设备连接，定时器应停止。
- `dotnet build .\TensileNeW.csproj` 必须通过；允许保留项目既有警告。
- 不提交这个调试功能相关改动；如用户要求提交，必须先提醒它被标记为本地调试辅助，按本节规则不应保存并提交。

## 启动网络检查和探测

程序启动加载动画窗口 `StartupWaitWindow` 期间会异步执行多项启动逻辑，包括试验指导文档缓存、字体准备和连接设备。连接设备前会先检查已连接的有线网卡是否存在与 `RAM.SettingModel.PLC_IP` 同网段的 IPv4 地址；如果没有同网段地址，启动阶段不申请管理员权限、不修改网络，直接跳过连接并进入主窗口失败状态。

主窗口显示连接失败弹窗 `ConnectionErrorDialog` 时提供“网络探测”入口。用户点击后，程序通过 UAC 启动同一 EXE 的提权探测进程，由 `Services/NetworkAdapterProbeService.cs` 依次处理所有已连接的有线网卡并跳过 Wi-Fi：

- 给当前有线网卡添加与 PLC 同网段的额外 IPv4 地址，目标是不修改原有地址。
- 提权进程只负责添加或移除候选额外 IPv4 地址，不负责连接 PLC，也不能占用 PLC 的 TCP 连接。
- 主进程在每次候选 IP 添加成功后，使用现有 `DataAqc` / `DeltaPLC2` 通信链路尝试连接 PLC。
- 如果主进程连接失败，移除本次刚添加的额外 IP，再尝试下一个候选地址；如果成功，保留这个额外 IP。

修改此逻辑时要特别注意：只能删除本次探测新增的额外 IP，不能删除或修改用户原有 IP；不要同时给多个网卡保留同一设备网段地址；提权探测失败、用户取消 UAC、设置失败和所有端口失败都需要给出可理解的提示。网络探测问题应限制在网络配置和主窗口探测流程内处理，不应修改 `DeltaPLC2` 等底层 PLC 通讯封装的连接接口。PLC/TCP 连接权始终属于主进程：提权进程不能探测或持有 PLC TCP 连接，更不能让主进程同时再开第二条 PLC 连接。

网络探测可能耗时，必须接入 `StartupWaitWindow` 这类加载动画窗口反馈进度。主 UI 线程只能负责显示窗口、更新提示和处理结果，耗时连接、探测、提权进程等待、PLC 重连等操作必须放在后台任务或异步等待中执行，不能使用同步阻塞等待卡住动画绘制或主窗口响应。
网络探测等待必须有明确超时边界：主窗口等待提权网络配置进程、单次 `netsh` 命令和主进程单次 PLC 连接尝试都不能无限等待；超时后必须关闭等待窗口并给出失败提示。
网络探测期间必须暂停 `DataAqc.Refresh()` 里的自动重连，避免后台自动重连与主窗口探测连接同时争用 PLC 连接锁或旧 socket 状态；探测结束后再恢复自动重连。当前实现是 `DataAqc.AutoReconnectSuspended` 标志位：主窗口 `RunNetworkProbeAndReconnectAsync` 在探测开始前置 `true`，`finally` 里恢复 `false`；`Refresh()` 主循环开头和异常分支都会判断该标志，命中则跳过本轮读取和 `TryReconnect()`，避免争用 `PlcConnectionLock`。修改探测或采集循环时必须保留这一对暂停/恢复点。
另外，`netsh add address` 之后 Windows 路由表/源地址选择存在几百毫秒的就绪窗口，单次连接尝试不够稳定。`MainWindow.TryConnectWithRetriesAsync` 在每个候选 IP 上做有限次数的连接重试（每次间隔约 1 秒），并在每次重试前检查整体探测超时；调整探测节奏时不要把候选 IP 退化回单次尝试。

## 配置和运行数据

- `Setting.json`：运行时配置文件，由 `RAM.Init()` 在程序目录读取或创建。
- `package.config`：独立的 AES 加密试用标记文件，不在界面或 `Setting.json` 中展示和设置。配置包含试用标记、启动次数和数据保存次数；仅试用版会在启动后递增启动次数，并在“保存数据和报告”或回放页“保存数据表格”完成后递增数据保存次数，完整版不读写这些计数。试用版的启动次数恰为 5、20、50、100 时，主窗口显示后弹出一次购买完整版提示，不执行其他操作。系统设置页始终显示当前版本，只有试用版显示这两项计数。非调试启动时，试用包以 `%LocalAppData%\ECS\package.config` 为权威副本：该副本存在则覆盖修复运行目录中缺失或内容不一致的文件；副本不存在时，试用包会将运行目录文件复制到该处，运行目录文件也不存在时先创建新的试用文件。运行目录自带完整版文件时，先检查 AppData 是否同为有效完整版：不是则由运行目录文件覆盖 AppData；是则由 AppData 文件覆盖修复运行目录。附加编译器调试器时不访问文件，Debug 编译为非试用、Release 编译为试用。builder 打包前通过控制台 Y/N 问询生成试用或非试用标记。该状态当前不得用于增加试用限制或其他业务逻辑。
- `package.config` 的试用标记、计数后允许追加权限文件同步跳过标记；旧文件没有该字段时按原有同步逻辑处理。携带该标记的非试用包只读取运行目录中的标记，完全跳过 `%LocalAppData%\ECS\package.config` 的读取、写入、复制、同步和覆盖；说明书缓存仍与权限文件无关，保持原有缓存逻辑。
- builder 生成试用包时，仅在输出目录名末尾追加 `-试用版`；非试用包保持原有目录名。builder 首先通过 `Y/N` 选择是否试用，回答 `N` 后再选择 `1`（带完整版权限配置文件）或 `2`（不带完整版权限配置文件）。
- 安装器生成 payload 时由安装器自己的可见 UTF-8 控制台执行与 builder 相同的两步选择，并将两个选择作为参数传给 builder；builder 收到完整参数后不得再次询问。
- `Assets/DefaultRecipe.json`：内置默认配方资源，构建时复制到输出目录。
- `NLog.config`：日志配置，构建时复制到输出目录。
- `TrialDataStore` 相关数据、日志、临时数据库等属于运行或验证产物，不应留在仓库中。

## 算法整合数据导出

主窗口“保存数据和报告”按钮下方提供“额外保存算法整合数据”复选框，默认勾选。勾选时，保存流程在原始数据 Excel 和试验报告之外，额外生成一个独立 Excel 文件，命名为 `{原基础文件名}_算法整合数据.xlsx`；不要把算法整合结果插入原始 Excel，也不要改变报告中的原始曲线截图。

算法实现位于 `Services/DisplacementResamplingService.cs`。位移间隔按 `速度设定 / 20` 计算，速度为 `1mm/s` 时对应 `0.05mm`；速度解析失败时才回退到 `0.05mm`。横轴使用 `Loadmodel.RealDistance`，力值使用相邻原始点线性插值得到，压边力和时间字段作为辅助列同步插值或透传。导出范围需要覆盖原始位移范围外侧的整间隔边界：起点向下取整、终点向上取整，边界点使用首尾相邻两点做线性外推，不能只保留内部插值点。保存事件中应先对 `DataAqc.loadModels` 做快照，再在线程池中生成额外文件，避免 UI 线程卡顿，也避免计算过程中实时采集列表继续变化影响本次算法数据。

隐藏调试入口：管理员密码窗口输入 `datai` 时，主窗口会弹出原始数据 Excel 文件选择框，选择 `.xlsx` / `.xls` 后由 `Services/DebugAlgorithmExcelService.cs` 读取第一张表并复用 `DisplacementResamplingService.SaveResampledDataToFile()` 生成同目录 `{原文件名}_整合数据_debug.xlsx`。该入口只用于离线验证算法整合数据，不应改变正常“保存数据和报告”流程，不应写入 `Setting.json`，也不应直接读写实时采集队列。

修改配置模型时，需要同时考虑旧 `Setting.json` 的兼容性，避免空值导致启动失败。

## 版本管理

- 应用版本统一维护在 `TensileNeW.csproj` 的 `Version`、`AssemblyVersion`、`FileVersion` 和 `InformationalVersion`。
- 版本号格式为 `主版本.次版本.修订版本.年月`，例如 `1.1.1.2606` 表示 1.1.1 版本、2026 年 6 月；即使只提升主版本、次版本或修订版本，也必须保留 `.年月` 后缀。`InformationalVersion` 使用带 `V` 前缀的显示格式，例如 `V1.1.1.2606`。
- 主窗口标题运行时读取程序集 `InformationalVersion`，不要在 XAML 中硬编码版本号。
- `builder/` 专门负责打包发布，打包目录名由 builder 读取主项目的 `InformationalVersion` 生成；更新版本时优先修改主项目版本元数据，不要在 builder 输出目录或生成产物里手工改版本。

## 绿色版打包

发布包必须是严格绿色版：拷贝到一台没有 .NET 或其他额外运行时环境的 Windows 电脑后，解压即可运行，不允许弹出安装 .NET 8 或其他运行时的系统提示。

`builder/` 打包主项目时必须使用 `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true` 生成单文件自包含包，不能退回框架依赖发布，也不能改回多文件 `lib/` 收纳布局。即使 `Builder.exe` 自身用 Debug 运行，生成的目标程序也必须是 Release。

单文件发布会把全部托管程序集、.NET 运行时和原生库（SkiaSharp、e_sqlite3、glfw3 等）打进唯一的 `{AssemblyName}.exe`，因此发布目录根部不再有散落的业务或第三方 DLL，也不再有 `lib/`、`runtimes/`、`ECS.dll`、`ECS.deps.json`、`ECS.runtimeconfig.json`。builder 必须断言根部 `*.dll` 数量为 0，否则视为单文件发布回退失败并报错，不能输出残留多文件包。

发布目录根部允许且应当保留的项：`{AssemblyName}.exe`（单文件主程序）、`NLog.config`、启动诊断脚本 `start-{AssemblyName}.cmd`、`Assets/`、`manuals/`。运行 EXE 后系统会在临时目录解包原生库，这是单文件机制的正常行为，不要尝试改成自定义解包目录。

由于单文件已内嵌运行时，不再需要把依赖 DLL 移动到 `lib/`，也不再需要改写 `.deps.json` 的运行时路径；相关收纳和改写逻辑已从 builder 删除，不要重新引入。`hostfxr.dll`、`hostpolicy.dll`、`coreclr.dll`、`clrjit.dll`、`System.Private.CoreLib.dll` 等启动核心文件同样被打进单文件，不会出现在根部。

启动诊断脚本是绿色版的一部分，不能删。`builder/` 必须生成并保留 `start-{AssemblyName}.cmd`，脚本负责切到程序目录、运行 EXE、写入 `{AssemblyName}-startup.log`，并在非零退出码时显示日志和暂停，便于在无环境机器上排查启动失败。清理发布产物时可以删除 `.pdb`、`.xml`、`createdump.exe` 和无用语言资源目录，但不能删除 `.cmd`、启动日志或其他用于排查运行失败的工具。

验证绿色版时，除构建成功外，还要检查发布目录根部 `*.dll` 数量为 0、`{AssemblyName}.exe` 存在、`NLog.config` 存在、`start-{AssemblyName}.cmd` 存在；必要时在无 .NET 的干净 Windows 环境实机验证单文件 EXE 能直接启动。

注意单文件模式下 `System.Reflection.Assembly.Location` 会返回空字符串（编译器 IL3000 警告），涉及程序目录定位的代码必须优先使用 `Environment.ProcessPath` 或 `AppContext.BaseDirectory`，不能依赖 `Assembly.Location`。当前 `ManualDocumentService` 已用 `Environment.ProcessPath` 优先，修改这些路径逻辑时要保持单文件兼容。

调试 builder 时不要把 `outputRoot` 指到仓库根目录：builder 会把 `outputRoot` 的上级目录当成历史发布目录清理，可能误删仓库里的 `NLog.config` 等文件。验证时应使用仓库外的临时目录作为输出根。

## 独立安装器

`installer/Installer` 是独立安装器项目，`installer/InstallerBuilder` 是独立控制台打包程序。界面和部署逻辑不应塞进主程序。InstallerBuilder 在运行时询问试用版本，调用 `builder/Builder` 生成绿色版 payload、压缩为临时 `payload.zip`、再发布安装器到自身运行目录的 `publish/`；WPF 安装器项目本身不得在 MSBuild 目标中自动生成 payload 或 publish。用户运行安装器 EXE 时不能再要求 builder、源码仓库或 .NET SDK 存在。

安装器运行时职责：

- 显示写死的安装界面、纯线条 Logo 和部署动画，不读取主程序主题。
- 欢迎页标题中的 `ECS` 保持原有居中布局；仅试用安装包在其右侧显示不参与标题布局的紫色“试用版”悬浮气泡。完整版不显示该气泡。
- 允许用户选择部署路径，默认使用当前用户可写路径，尽量避免管理员权限。
- 可选创建当前用户桌面快捷方式，不写注册表，不注册卸载项。
- 释放嵌入的绿色版发布包后，静默生成 Word/PPT 说明文件的 XPS 缓存；没有 Office/WPS 或转换失败时必须静默跳过，不弹错误提示。
- 完成后显示“部署成功”，提供“启动 ECS”和“关闭”。

验证安装器时，不能从 `installer/Installer/bin/Debug`、普通 `bin/Release` 或项目中间目录拿交付物；这些普通 build 产物可能依赖运行时，也不会内嵌 payload，不是最终安装器。最终交付物由 `InstallerBuilder` 运行并发布到其运行目录下的 `publish/ECS-Installer.exe`，该目录必须没有散落 DLL。安装器构建过程中的 payload zip 只能临时生成在系统临时目录，不能保存在 `installer/` 源码目录、仓库 `bin/` 或其他可提交位置。运行后应能释放 `ECS.exe`、`NLog.config`、`start-ECS.cmd`、`Assets/`、`manuals/` 并可创建桌面快捷方式。不要把安装验证产生的临时部署目录、缓存文件或日志留在仓库中。

## 编码规则

- 所有新增或修改的文件必须使用 UTF-8 编码，避免引入乱码。
- 发现已有文件存在乱码，或文件编码不符合 UTF-8 时，应当将该文件修复为 UTF-8，并确保中文内容恢复为可读文本。
- 修改 XAML、C#、JSON、BAS 生成逻辑时，注意中文文案和注释在 PowerShell 控制台里可能显示受代码页影响；以文件实际 UTF-8 内容为准。
- 不要仅凭 PowerShell 控制台显示判断中文文件已经损坏；必要时用支持 UTF-8 的编辑器或二进制/编码检查方式确认。

## 修改约束

- 保持现有 WPF、HandyControl、MahApps、ScottPlot 和 MVVM Toolkit 的使用方式，不为局部修改引入新的 UI 框架或大规模重构。
- 修改主界面 UI 前必须先确认所在 Grid/Border 的固定宽高、Margin、Padding 和可用空间，新增按钮、图标或文字后要按实际可用宽度核算总宽高，避免折叠、遮挡、显示不全或挤压相邻控件。
- 新增弹窗、列表和局部面板时，默认不要添加装饰性外框或边框，尤其不要把 `AppLayoutBorderBrush`、`AppStartupWaitBorderBrush` 等主题强调色当作普通容器边框使用；只有现有控件模板或明确设计需要边界时才保留必要分隔线。
- 主界面按钮应优先复用现有 `ActionButtonStyle`、`IndicatorActionButtonStyle`、`ButtonPrimary`、`ButtonDanger` 等本地样式和动态主题资源；除非确有必要，不要临时手写一套背景色、边框色、字体或高度，避免破坏主题一致性。
- 在 DataGrid、曲线图、预览区等内容控件上叠放小按钮时，必须给表头文字、滚动条和内容区域预留空间；不能让按钮覆盖关键数据、列标题或交互区域。必要时通过缩小按钮、调整列宽、增加右侧 Padding/Margin 或使用独立工具列解决。
- 隐藏某个 UI 开关时，不应只隐藏文字和选择框后留下空白承载行；要同时评估承载它的行高、背景块和相邻按钮布局是否还需要保留、压缩或移位。
- 修改 XAML 布局后，除 `dotnet build .\TensileNeW.csproj` 外，还应人工检查关键窗口在目标尺寸下是否存在裁切、重叠、折叠、显示不全和主题不一致；无法运行界面时必须在回复中明确说明只做了静态尺寸核算和构建验证。
- 修改 PLC 通信逻辑时，要确认是否影响启动连接、手动重连、自动重连和采集循环。
- 修改配方逻辑时，要确认内置配方和用户配方的保存边界。`RAM.NormalizeUserRecipes()` 会过滤内置配方，避免把内置配方写入用户配置。
- 修改试验指导文档逻辑时，要确认 `manuals/` 资源复制、缓存生成、PDF/Word/PPT 内嵌预览和缺少 Office/WPS 时的降级提示。
- 修改曲线或数据表逻辑时，要确认主窗口内嵌显示和独立窗口显示都能正常工作。
- 如果新增了本文件没有提及的新功能、新逻辑或新文件，应同步更新 `AGENTS.md`，补充职责、约束和验证注意事项。

## 验证和清理

常用验证命令：

```powershell
dotnet build .\TensileNeW.csproj
```

如果为了隔离验证输出而指定临时输出目录，验证后必须清理对应目录。

不要把验证、构建或检查产生的中间产物留在仓库里，包括但不限于：

- 临时 `bin` 子目录。
- 临时 `obj` 子目录。
- 临时数据库。
- 临时 Basic 文件。
- 日志文件。
- 发布或打包过程产生的临时目录。

删除任何验证产物前，必须确认目标路径位于本仓库或明确的临时目录内。不要对不确定路径执行递归删除。

## Git 注意事项

- `AGENTS.md` 是本地协作说明文件，按仓库规则被 Git 忽略，不应为了提交而取消忽略或强行追踪。
- 更新 `AGENTS.md` 后不需要验证它是否出现在 `git diff`；以文件实际内容为准。
- 仓库可能存在用户未提交的工作区变更。不要回退、覆盖或格式化与当前任务无关的文件。
- `release` 分支用于保存此前所有发布版本组成的版本链，每个版本应是一个以版本号命名的压缩提交，例如 `V1.1.2.2606`。不要把 `master` 的全部开发提交历史直接 merge 进 `release`，也不要生成包含整条 `master` 历史的 merge commit。
- 发布新版本到 `release` 时，先确认 `release` 当前最新发布提交与 `master` 对应旧版本提交的文件树一致，再把从该旧版本到当前版本的最终文件树压成一个新提交。新提交必须以 `release` 当前最新发布提交为唯一父提交，确保 `release` 历史继续保留此前所有版本，再追加当前版本。
- 如果 `release` 的发布版本是由 `master` 旧版本压缩得到，两个分支可能没有共同祖先；这种情况下不要使用 `git merge --allow-unrelated-histories` 把历史硬合进来。应使用等价文件树确认后创建单父压缩提交，并在操作后比较 `release` 与 `master` 当前文件树无差异。

## 摄像头采集和预览

摄像头功能使用 Windows 官方 WinRT API，不引入第三方摄像头库。核心封装在 `Services/CameraCaptureService.cs`：

- 设备枚举使用 `DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)`，配置保存设备 `Id` 和 `Name`。
- 连接使用 `MediaCapture.InitializeAsync()`，通过 `MediaCaptureInitializationSettings.VideoDeviceId` 指定已选择设备。
- 帧读取使用 `MediaFrameReader`，帧数据统一转换为 `SoftwareBitmap` 的 `Bgra8/Premultiplied` 格式，再写入 WPF `WriteableBitmap`。
- WPF 不直接渲染摄像头原始帧；不要绕过 `SoftwareBitmap.Convert` 和 `WriteableBitmap` 这条转换链。

启动阶段在 PLC 连接尝试之后、主窗口创建之前扫描摄像头，并接入 `StartupWaitWindow` 等待提示。首次启动且扫描到摄像头但配置为空时，必须弹出 `CameraSelectionWindow` 让用户选择，并把选择保存到 `Setting.json`。后续启动如果扫描到已保存设备则自动连接；如果保存的设备未扫描到或连接失败，主窗口显示摄像头连接失败弹窗，但不应阻塞 PLC 和主程序启动。

系统设置页的摄像头下拉框绑定启动时扫描到的设备列表，保存时更新 `SettingModel.CameraDeviceId` / `CameraDeviceName` 并按当前选择重连预览。主页左侧试验参数下方显示摄像头预览，双击预览区域打开 `CameraPreviewWindow` 独立预览窗口；独立窗口必须使用当前主题标题栏，并允许拖动和调整大小。主页摄像头预览右侧的刷新按钮只在当前流断开或无画面时异步重连；正常播放时点击不做任何操作，重连结果只通过主窗口 Growl 提示。

首次选择摄像头的 `CameraSelectionWindow` 必须沿用 HandyControl `Dialog.Show` 的弹窗风格，并在下拉框下方提供小预览画面。该预览必须借用主窗口持有的唯一 `CameraCaptureService` 实例，下拉框切换时异步切换同一条连接；用户确认后主窗口直接接管这条已打开连接，不允许释放后再重连，也不允许创建第二个摄像头服务实例与正式预览争用同一个摄像头。

摄像头采集和 UI 刷新必须保持异步，不要在 UI 线程同步等待 `MediaCapture.InitializeAsync()`、`MediaFrameReader.StartAsync()` 或帧读取。摄像头连接失败不能影响 `DataAqc.Refresh()`、PLC 自动重连、曲线刷新和试验数据采集。
