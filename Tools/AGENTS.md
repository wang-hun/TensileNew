# Tools 模块导航

| 责任 | 文件 / 类型 | 入口 | 边界 |
|---|---|---|---|
| 当前 PLC 通信 | `DeltaPLC2.cs` / `DeltaPLC2` | Modbus TCP 读写、连接与释放 | 当前运行主路径；生命周期由 `DataAqc` 协调 |
| 地址映射 | `ModbusAddressHelper.cs` | 地址解析 / 转换 | 与 PLC 变量表约定同步，不在窗口重复换算 |
| 旧实现 | `DeltaPLC.cs` | 无当前运行入口 | 不作为新功能实现基础，除非明确迁移任务 |

PLC 业务入口和采集循环见 `../Models/AGENTS.md`；冲程压边按键即时写入，D40 版本只在连接成功时读取并缓存，按键不得重新读取。提权探测子进程不得建立 PLC TCP 连接。
