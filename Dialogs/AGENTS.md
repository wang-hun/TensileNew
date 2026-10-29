# Dialogs 模块导航

弹窗负责收集输入或展示状态，通过事件 / 返回值交还窗口协调；不在弹窗里建立 PLC 连接、操作采集队列或直接持久化产品状态。

| 工作流 | 文件 | 类 / 事件 | 上游与下游 |
|---|---|---|---|
| 启动等待 | `StartupWaitWindow.xaml.cs` | `StartupWaitWindow`、`BackgroundStartupWaitWindow` | 启动流程见 `../App.xaml.cs`；后台等待窗使用独立 STA Dispatcher |
| 连接失败 | `ConnectionErrorDialog.xaml.cs` | 网络探测请求事件 | 由 `MainWindow.Network` 接手 UAC 与 PLC 重连 |
| PIN / 隐藏入口 | `SettingsPinDialog.xaml.cs`、`SettingsPinWindow.xaml.cs` | PIN 验证与事件透传 | `MainWindow.LogoImage_MouseLeftButtonDown()` 订阅；算法导入只调用离线服务 |
| 曲线设置 / 清理 | `ChartSettingsWindow.xaml.cs`、`CurveFilterWindow.xaml.cs`、`ClearPlotConfirmDialog.xaml.cs` | 设置值、用户确认 | 窗口传入对应 `LoadPlotController`，遵循 `../Services/AGENTS.md` |
| 配方确认 / 命名 | `RecipeConfirmDialog.xaml.cs`、`RecipeNameDialog.xaml.cs` | 确认 / 命名结果事件 | MainWindow 交给 `MainViewModel` 执行写入 |
| 相机选择 | `CameraSelectionWindow.xaml.cs` | 设备选择事件 | MainWindow 交给 `CameraCaptureService` |
| 回放和数据 | `LoadDataWindow.xaml.cs`、`PlotWindow.xaml.cs`、`VariableWindow.xaml.cs` | 展示 / 用户交互 | 数据源分别由 TrialDataStore、LoadPlotController、DataAqc 提供 |

新增或修改弹窗时保留 HandyControl / WPF 方式，不添加装饰性主题边框；布局遵守根 `AGENTS.md` 尺寸核算规则。
