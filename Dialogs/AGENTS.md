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
| 报告 PDF 预览与伪编辑 | `ReportPreviewEditorWindow.xaml(.cs)`、`ReportEditorDocument`、`IReportPdfExporter` | PDF 页面按物理尺寸计算逻辑大小，至少以 300 DPI 栅格化；适配或放大时按实际显示倍率和设备缩放比例提高目标像素并重新渲染，避免低分辨率位图被 `Viewbox` 拉伸；多页滚动预览默认适配可视宽度，窗口变化时重新适配；缩放按钮和 Ctrl+滚轮统一缩放整页及字段叠层；确认返回字段值，取消丢弃 | “案例”和“空白”PDF 从 `doc/` 复制到运行目录 `ReportTemplates/`，预览显示空白 PDF；确认后将案例 PDF 原样复制到目标 PDF 作为测试输出。导出接口不接收字段值，字段按案例和空白 PDF 对照映射到页面，日期以 dd/MM/yyyy 输入和回收；其他字段记忆保存在 `%LocalAppData%\ECS\ReportEditorValues.json`，日期每次打开默认为本地当天且不持久化；PDF 组装未实现。使用现有 PdfiumViewer 渲染页面；本阶段不新增 PDF 依赖 |

新增或修改弹窗时保留 HandyControl / WPF 方式，不添加装饰性主题边框；布局遵守根 `AGENTS.md` 尺寸核算规则。
