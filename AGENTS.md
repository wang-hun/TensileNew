# AGENTS.md

本文件是本仓库的本地协作说明，用于让后续维护者和自动化编码代理快速理解项目结构、关键约束和验证方式。它不应作为产品功能文档对外发布。

## 项目概览

这是一个基于 WPF 的 .NET 8 Windows 桌面程序，项目文件为 `TensileNeW.csproj`，程序集名为 `ECS`。程序面向拉伸/成形试验设备，核心职责包括：

- 通过 Modbus TCP 连接台达 PLC。
- 读取实时力、位移、速度、压边力、拉伸时间等 PLC 数据。
- 写入配方参数和线圈控制指令。
- 展示实时曲线、数据表格、配方管理、试验说明文档和测试报告相关界面。

主窗口和主要交互在 `MainWindow.xaml`、`MainWindow.xaml.cs` 和 `MainViewModel.cs` 中。应用启动入口在 `App.xaml.cs`。

## 目录职责

- `Models/`：运行状态、配置、配方、PLC 变量、试验数据等模型。`RAM.cs` 负责加载和保存 `Setting.json`，`DataAqc.cs` 负责 PLC 变量初始化、连接、采集循环和数据队列。
- `Tools/`：PLC 通信和工具类。当前实际通信类是 `DeltaPLC2.cs`，使用 `NModbus`；`DeltaPLC.cs` 是旧的手工 Modbus TCP 实现，当前不作为运行主路径。
- `Services/`：曲线控制、说明文档缓存、试验报告、试验数据存储等服务。
- `Dialogs/`：设置、配方、数据窗口、曲线窗口、启动等待窗口等 WPF 弹窗。
- `Controls/`：复用控件，例如说明文档查看器；文档预览只保留 PDF、Word、PPT 内嵌显示，不再引入 WebView/WebView2 这类内嵌浏览器依赖。
- `Themes/`：颜色方案和主题资源。
- `Assets/`：图标、Logo、默认配方和字体资源。
- `manuals/`：发布包中的试验指导文档目录，支持 PDF、Word、PPT 文档通过 XPS 预览控件内嵌显示。
- `builder/`：打包/发布辅助项目，不参与主项目编译。

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
- `Assets/DefaultRecipe.json`：内置默认配方资源，构建时复制到输出目录。
- `NLog.config`：日志配置，构建时复制到输出目录。
- `TrialDataStore` 相关数据、日志、临时数据库等属于运行或验证产物，不应留在仓库中。

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

## 编码规则

- 所有新增或修改的文件必须使用 UTF-8 编码，避免引入乱码。
- 发现已有文件存在乱码，或文件编码不符合 UTF-8 时，应当将该文件修复为 UTF-8，并确保中文内容恢复为可读文本。
- 修改 XAML、C#、JSON、BAS 生成逻辑时，注意中文文案和注释在 PowerShell 控制台里可能显示受代码页影响；以文件实际 UTF-8 内容为准。
- 不要仅凭 PowerShell 控制台显示判断中文文件已经损坏；必要时用支持 UTF-8 的编辑器或二进制/编码检查方式确认。

## 修改约束

- 保持现有 WPF、HandyControl、MahApps、ScottPlot 和 MVVM Toolkit 的使用方式，不为局部修改引入新的 UI 框架或大规模重构。
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
