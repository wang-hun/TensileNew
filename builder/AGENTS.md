# Builder 发布流程导航

| 流程 | 文件 / 类 / 入口 | 修改边界 |
|---|---|---|
| 控制台选择与打包 | `Builder/Program.cs`、`Builder` | 先问是否试用；非试用再选择完整权限配置 `1` 或不携带 `2`；安装器传入完整参数时不重复询问 |
| 物理尺寸标定 | `Builder/Program.cs`、`RingPhysicalCalibration.cs` | 新视觉包必须在输出清理前弹出原生文件选择窗口选择并验证 DICE calibration JSON，不要求输入路径；无效文件提示并允许重选，取消则中止打包且不清理输出；复制为运行目录根部 `dice-calibration.json`；无效文件不得继续发布 |
| 产品发布 | `Builder` 对主项目执行 `dotnet publish` | 目标固定为 `TensileNeW.csproj` Release、win-x64、自包含、单文件；不得把 `demo/`、`SamplingCapture/`、builder 或 installer 编入产品 |
| 产物目录 | builder 发布目录生成逻辑 | 读取主项目 `InformationalVersion` 生成目录名；只给试用版目录名追加 `-试用版` |
| 单文件断言 | 发布清理 / 校验流程 | 根目录 DLL 必须为 0；EXE、`NLog.config`、`start-ECS.cmd`、`Assets/`、`manuals/` 必须保留 |
| 外置识别程序 | `Builder/Program.cs` | 新视觉包选择为 Y 时将指定 publish 目录全部复制到 `RingInspect/`，并在 ECS 根目录旁生成 `RingInspect.lnk`；选择 N 时不附带 |
| 启动脚本 | `start-ECS.cmd` 生成逻辑 | 切换程序目录、运行 EXE、保存启动日志，非零退出时展示日志并暂停 |

不要在仓库根目录执行 builder 输出验证：builder 会清理历史发布目录。使用仓库外临时输出根。完整绿色版验收标准见仓库根 `AGENTS.md`。

- Builder 主入口必须为 STA；启用 WPF 仅为调用原生文件选择窗口，必须保留 `OutputType=Exe` 和 `DisableWinExeOutputInference=true`，不能丢失控制台。显式传入标定文件 CLI 参数时仍验证文件，不重复弹窗。

- 控制台新视觉选择后询问相机品牌（1 华睿 / 2 大恒），安装器将选择传给 builder；使用 CameraSdkBrand 条件编译且只发布对应托管 SDK，华睿使用 MVSDK_Net.dll，大恒使用现有 GxIAPINET.dll，原生运行库由目标机安装，禁止复制或打包 CameraSdk/Daheng/Native。publish 使用输出根下独立中间目录防止品牌切换复用旧 deps.json，完成后清理，写入 Setting.json 的 VisionCameraBrand，断言另一品牌及额外原生目录不存在。
