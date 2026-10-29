# Models 模块导航

## 流程定位

| 流程 | 文件 | 类 / 函数 | 修改边界 |
|---|---|---|---|
| 配置读写 | `RAM.cs`、`SettingModel.cs` | `RAM.Init()`、配置保存入口、`SettingModel` | 新字段兼容旧 `Setting.json` 和空值；不将权限 / 试用状态混入用户设置 |
| PLC 变量及采集 | `DataAqc.cs` | `InitVariables()`、`TryConnect()`、`Refresh()`、`StartConsumers()`、`ApplyBatchOnUiThread()`、`Enqueue()` | 真实 PLC 采样主链；不得在循环里加入非采集逻辑 |
| PLC 快照和试验点 | `PLCVariable.cs`、`Loadmodel.cs` 及相关模型 | 属性与数据映射 | 维护寄存器地址、类型及五列原始数据语义 |
| 配方 | `RecipeModel.cs` 及配方模型 | 模型属性 / 序列化 | 写 PLC 的协调逻辑位于 `MainViewModel` |

`../MainViewModel.cs` 的 `MainViewModel` 是 UI 与数据 / PLC 命令之间的产品交互层：PLC 脉冲走 `PulseAsync()` / `TryPulseAsync()` / `PulseExclusiveAsync()`，持续线圈走 `SetBoolAsync()` / `SetStrokeStampingAsync()`，配方写入走 `WriteRecipeAsync()`，配置持久化走 `SaveSettings()` / `SaveSettingsAndApplyLanguage()`，试验序列号走 `AdvanceTrialSerialNumber()` / `ResetTrialSerialNumber()`。不要在 `MainWindow` code-behind 重复这些业务写入。

## PLC 关键约束

- 当前通信实现为 `Tools/DeltaPLC2.cs`；`Tools/DeltaPLC.cs` 是旧实现。地址换算由 `Tools/ModbusAddressHelper.cs` 管理。
- Unit ID 默认 1、端口默认 502；修改连接、重连或采集须检查后台线程、连接锁和 UI Dispatcher。
- 连接成功后只读一次 `D40` 版本并缓存冲程压边目标线圈；按键只使用缓存值。完整契约见根 `AGENTS.md` 的“PLC 与实时数据”。
- `Refresh()` 的 `M111` 完全复位是设备真实链路，不能被本地调试功能删除、跳过或改写。
- `AutoReconnectSuspended` 在网络探测期间阻止自动重连；恢复点必须位于探测 `finally`。
- `StartConsumers()` 批次和 50ms 调度不因采样率显示而改变；采样率旁路监听 `UiBatchApplied`。

## 本地正弦调试例外

仅用户明确要求恢复时实施，且不得作为产品功能提交。详细规则见根 `AGENTS.md` 的“本地调试”和 `../MainWindow/AGENTS.md`。允许新增的 `DataAqc` 清空入口只能清 `_queue`、`loadModels` 并触发 `ChartCleared`；生成点必须走 `Enqueue()`。
