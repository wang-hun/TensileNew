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
- `Controls/`：复用控件，例如说明文档查看器。
- `Themes/`：颜色方案和主题资源。
- `Assets/`：图标、Logo、默认配方和字体资源。
- `helper/`：随程序分发的本地帮助文档和图片资源。
- `builder/`：打包/发布辅助项目，不参与主项目编译。

## 启动流程

`App.xaml.cs` 的 `Application_Startup` 是启动主线：

1. 调用 `RAM.Init()` 读取或创建配置。
2. 应用主题和七段数码字体资源。
3. 调用 `DataAqc.InitVariables()` 初始化 PLC 变量和 `DeltaPLC2` 客户端对象。
4. 显示 `StartupWaitWindow`，准备帮助文档缓存。
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

程序启动加载动画窗口 `StartupWaitWindow` 期间会异步执行多项启动逻辑，包括帮助文档缓存、字体准备和连接设备。连接设备前会先检查已连接的有线网卡是否存在与 `RAM.SettingModel.PLC_IP` 同网段的 IPv4 地址；如果没有同网段地址，启动阶段不申请管理员权限、不修改网络，直接跳过连接并进入主窗口失败状态。

主窗口显示连接失败弹窗 `ConnectionErrorDialog` 时提供“网络探测”入口。用户点击后，程序通过 UAC 启动同一 EXE 的提权探测进程，由 `Services/NetworkAdapterProbeService.cs` 依次处理所有已连接的有线网卡并跳过 Wi-Fi：

- 给当前有线网卡添加与 PLC 同网段的额外 IPv4 地址，不修改原有地址。
- 使用该本地额外 IP 绑定 TCP 客户端，尝试连接 PLC 的 Modbus TCP 端口 502。
- 如果失败，移除本次刚添加的额外 IP，再尝试下一个端口。
- 如果成功，保留这个额外 IP，返回主进程并触发 PLC 重连。

修改此逻辑时要特别注意：只能删除本次探测新增的额外 IP，不能删除或修改用户原有 IP；不要同时给多个网卡保留同一设备网段地址；提权探测失败、用户取消 UAC、设置失败和所有端口失败都需要给出可理解的提示。

网络探测可能耗时，必须接入 `StartupWaitWindow` 这类加载动画窗口反馈进度。主 UI 线程只能负责显示窗口、更新提示和处理结果，耗时连接、探测、提权进程等待、PLC 重连等操作必须放在后台任务或异步等待中执行，不能使用同步阻塞等待卡住动画绘制或主窗口响应。

## 配置和运行数据

- `Setting.json`：运行时配置文件，由 `RAM.Init()` 在程序目录读取或创建。
- `Assets/DefaultRecipe.json`：内置默认配方资源，构建时复制到输出目录。
- `NLog.config`：日志配置，构建时复制到输出目录。
- `TrialDataStore` 相关数据、日志、临时数据库等属于运行或验证产物，不应留在仓库中。

修改配置模型时，需要同时考虑旧 `Setting.json` 的兼容性，避免空值导致启动失败。

## 编码规则

- 所有新增或修改的文件必须使用 UTF-8 编码，避免引入乱码。
- 发现已有文件存在乱码，或文件编码不符合 UTF-8 时，应当将该文件修复为 UTF-8，并确保中文内容恢复为可读文本。
- 修改 XAML、C#、JSON、BAS 生成逻辑时，注意中文文案和注释在 PowerShell 控制台里可能显示受代码页影响；以文件实际 UTF-8 内容为准。
- 不要仅凭 PowerShell 控制台显示判断中文文件已经损坏；必要时用支持 UTF-8 的编辑器或二进制/编码检查方式确认。

## 修改约束

- 保持现有 WPF、HandyControl、MahApps、ScottPlot 和 MVVM Toolkit 的使用方式，不为局部修改引入新的 UI 框架或大规模重构。
- 修改 PLC 通信逻辑时，要确认是否影响启动连接、手动重连、自动重连和采集循环。
- 修改配方逻辑时，要确认内置配方和用户配方的保存边界。`RAM.NormalizeUserRecipes()` 会过滤内置配方，避免把内置配方写入用户配置。
- 修改帮助文档逻辑时，要确认 `helper/` 资源复制、缓存生成和缺少 Office 时的降级提示。
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
