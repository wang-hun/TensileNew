# Installer 安装流程导航

| 流程 | 位置 | 修改边界 |
|---|---|---|
| 安装器 UI / 部署 | `Installer/` WPF 工程 | 独立安装、用户可写目录、可选当前用户桌面快捷方式；不写注册表或卸载项 |
| payload 构建 | `InstallerBuilder/` | 可见 UTF-8 控制台完成与 builder 相同的试用 / 权限两步选择，将参数传给 builder；payload ZIP 仅放系统临时目录 |
| 最终交付物 | `InstallerBuilder` 发布入口 | 最终 EXE 位于其运行目录 `publish/ECS-Installer.exe`，不得从普通 `bin/` 或 `Installer/bin/` 取交付物；publish 根部不得散落 DLL |
| 运行时释放 | `Installer/` | 安装 EXE 必须独立工作，不依赖源码、builder 或 SDK；释放单文件产品包内容 |
| Office 文档缓存 | 安装后静默流程 | Word/PPT XPS 缓存失败或无 Office/WPS 时静默跳过 |

不得让 WPF Installer 项目的 MSBuild 自动生成 payload / publish；验证部署目录、缓存、日志不得留在仓库。
