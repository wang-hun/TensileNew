# Builder 发布流程导航

| 流程 | 文件 / 类 / 入口 | 修改边界 |
|---|---|---|
| 控制台选择与打包 | `Builder/Program.cs`、`Builder` | 先问是否试用；非试用再选择完整权限配置 `1` 或不携带 `2`；安装器传入完整参数时不重复询问 |
| 产品发布 | `Builder` 对主项目执行 `dotnet publish` | 目标固定为 `TensileNeW.csproj` Release、win-x64、自包含、单文件；不得把 `demo/`、`SamplingCapture/`、builder 或 installer 编入产品 |
| 产物目录 | builder 发布目录生成逻辑 | 读取主项目 `InformationalVersion` 生成目录名；只给试用版目录名追加 `-试用版` |
| 单文件断言 | 发布清理 / 校验流程 | 根目录 DLL 必须为 0；EXE、`NLog.config`、`start-ECS.cmd`、`Assets/`、`manuals/` 必须保留 |
| 启动脚本 | `start-{AssemblyName}.cmd` 生成逻辑 | 切换程序目录、运行 EXE、保存启动日志，非零退出时展示日志并暂停 |

不要在仓库根目录执行 builder 输出验证：builder 会清理历史发布目录。使用仓库外临时输出根。完整绿色版验收标准见仓库根 `AGENTS.md`。
