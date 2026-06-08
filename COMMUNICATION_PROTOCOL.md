# PLC 通信协议说明

本文档说明本项目与 PLC 通信时实际使用的 Modbus TCP 协议格式，以及项目中读写寄存器/线圈的代码位置。

## 1. 当前实际通信实现

项目当前实际使用的通信类是：

- `TensileNeW.Tools.DeltaPLC2`
- 文件：`Tools/DeltaPLC2.cs`
- 初始化位置：`Models/DataAqc.cs` 的 `DataAqc.InitVariables()`

```csharp
plc = new DeltaPLC2(RAM.SettingModel.PLC_IP);
```

`DeltaPLC2` 自身不手工拼接 Modbus 报文，而是通过 NuGet 包 `NModbus` 生成和解析报文：

```csharp
_master = new ModbusFactory().CreateMaster(Client);
```

项目引用：

```xml
<PackageReference Include="NModbus" Version="3.0.81" />
```

因此，当前运行时的 TCP 数据报由 `NModbus` 负责组装，`DeltaPLC2` 只是业务封装层。

另一个类 `Tools/DeltaPLC.cs` 是手工拼接 Modbus TCP 报文的旧实现，但当前项目未实例化使用。`Tools/DeltaPlcController .cs` 在 `.csproj` 中被 `Compile Remove` 排除，也不参与当前编译。

## 2. 连接参数

| 项目 | 值 |
|---|---|
| 协议 | Modbus TCP |
| 默认端口 | 502 |
| PLC IP | `RAM.SettingModel.PLC_IP`，默认值见 `Models/SettingModel.cs` |
| Unit ID / Slave ID | `1`，见 `DeltaPLC2(string ip, byte slaveId = 1)` |
| TCP 客户端 | `System.Net.Sockets.TcpClient` |

## 3. Modbus TCP 基本帧格式

Modbus TCP 报文由两部分组成：

```text
MBAP Header，7 字节 + PDU，长度可变
```

### 3.1 MBAP Header

| 字节序号 | 长度 | 字段 | 说明 |
|---:|---:|---|---|
| 0-1 | 2 | Transaction Identifier | 事务号。由客户端生成，用于请求/响应匹配。NModbus 自动维护，不保证固定为 `00 00`。 |
| 2-3 | 2 | Protocol Identifier | 协议号。Modbus TCP 固定为 `00 00`。 |
| 4-5 | 2 | Length | 后续字节长度，即 `Unit ID + PDU` 的长度。大端序。 |
| 6 | 1 | Unit Identifier | 从站 ID。本项目默认 `01`。 |

### 3.2 PDU

PDU 从字节 7 开始：

| 字节序号 | 长度 | 字段 | 说明 |
|---:|---:|---|---|
| 7 | 1 | Function Code | 功能码 |
| 8... | 可变 | Data | 功能码对应的数据区 |

所有 Modbus 地址、数量、寄存器值在报文中均按大端序传输，即高字节在前、低字节在后。

## 4. 地址换算

项目使用 `Tools/ModbusAddressHelper.cs` 将台达 PLC 地址转换为 Modbus 地址。

| 台达地址类型 | Modbus 十六进制地址计算 | 项目示例 |
|---|---:|---|
| `D` | `D地址 + 0x1000` | `D400 -> 0x1190` |
| `M` | `M地址 + 0x0800` | `M1 -> 0x0801` |
| `Y` | `Y地址 + 0x0500` | `Y4 -> 0x0504` |
| `X` | `X地址 + 0x0400` | - |
| `S` | `S地址` | - |
| `T` | `T地址 + 0x0600` | - |
| `C` | `C地址 + 0x0E00` | - |

例如：

```csharp
ModbusAddressHelper.ConvertToModbusAddresss("D400").HexAddress
```

返回 `0x1190`，业务代码再把它作为 `ushort` 地址传给 `DeltaPLC2`。

## 5. 本项目使用到的功能码

| 功能码 | 名称 | `DeltaPLC2` 函数 | NModbus 调用 |
|---:|---|---|---|
| `0x01` | 读线圈 | `ReadBool`, `ReadBools` | `ReadCoils` |
| `0x05` | 写单个线圈 | `WriteBool` | `WriteSingleCoil` |
| `0x0F` | 写多个线圈 | `WriteBools` | `WriteMultipleCoils` |
| `0x03` | 读保持寄存器 | `ReadUShort`, `ReadUShorts`, `ReadFloat`, `ReadFloats` | `ReadHoldingRegisters` |
| `0x06` | 写单个保持寄存器 | `WriteUShort`, `WriteInt` | `WriteSingleRegister` |
| `0x10` | 写多个保持寄存器 | `WriteUShorts`, `WriteFloat`, `WriteFloats` | `WriteMultipleRegisters` |

## 6. 功能码 0x03：读保持寄存器

项目函数：

- `DeltaPLC2.ReadUShort(ushort address)`
- `DeltaPLC2.ReadUShorts(ushort startAddress, ushort count)`
- `DeltaPLC2.ReadFloat(ushort startAddress)`，读取 2 个寄存器
- `DeltaPLC2.ReadFloats(ushort startAddress, ushort count)`，读取 `count * 2` 个寄存器

### 6.1 请求格式

总长度固定 12 字节：

```text
MBAP 7 字节 + PDU 5 字节
```

| 字节序号 | 长度 | 示例 | 字段 | 说明 |
|---:|---:|---|---|---|
| 0-1 | 2 | `00 01` | Transaction ID | 事务号，NModbus 自动生成 |
| 2-3 | 2 | `00 00` | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | `00 06` | Length | 后续 `Unit ID + PDU` 长度，固定 6 |
| 6 | 1 | `01` | Unit ID | 从站 ID，本项目默认 1 |
| 7 | 1 | `03` | Function Code | 读保持寄存器 |
| 8-9 | 2 | `11 90` | Start Address | 起始寄存器地址，例如 `D400 -> 0x1190` |
| 10-11 | 2 | `00 02` | Quantity | 读取寄存器数量，例如 Float 读取 2 个 |

示例：读取 `D400` 的一个 Float，即 2 个保持寄存器：

```text
00 01 00 00 00 06 01 03 11 90 00 02
```

### 6.2 正常响应格式

总长度：

```text
9 + N * 2 字节
```

其中 `N` 为返回寄存器数量。

| 字节序号 | 长度 | 示例 | 字段 | 说明 |
|---:|---:|---|---|---|
| 0-1 | 2 | `00 01` | Transaction ID | 与请求一致 |
| 2-3 | 2 | `00 00` | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | `00 07` | Length | `Unit ID + Function + ByteCount + Data` |
| 6 | 1 | `01` | Unit ID | 从站 ID |
| 7 | 1 | `03` | Function Code | 读保持寄存器 |
| 8 | 1 | `04` | Byte Count | 数据字节数，`N * 2` |
| 9... | 可变 | - | Register Data | 每个寄存器 2 字节，高字节在前 |

读取 2 个寄存器时，响应总长度为 13 字节。

## 7. 功能码 0x06：写单个保持寄存器

项目函数：

- `DeltaPLC2.WriteUShort(ushort address, ushort value)`
- `DeltaPLC2.WriteInt(ushort address, int value)`，内部转成 `ushort` 后调用 `WriteUShort`

### 7.1 请求格式

总长度固定 12 字节：

```text
MBAP 7 字节 + PDU 5 字节
```

| 字节序号 | 长度 | 示例 | 字段 | 说明 |
|---:|---:|---|---|---|
| 0-1 | 2 | `00 02` | Transaction ID | 事务号 |
| 2-3 | 2 | `00 00` | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | `00 06` | Length | 后续长度固定 6 |
| 6 | 1 | `01` | Unit ID | 从站 ID |
| 7 | 1 | `06` | Function Code | 写单个保持寄存器 |
| 8-9 | 2 | `11 9A` | Register Address | 寄存器地址，例如 `D410 -> 0x119A` |
| 10-11 | 2 | `00 64` | Register Value | 写入值，例如十进制 100 |

示例：向 `D410` 写入 `100`：

```text
00 02 00 00 00 06 01 06 11 9A 00 64
```

### 7.2 正常响应格式

正常响应会回显请求的 PDU，因此总长度也是 12 字节：

| 字节序号 | 长度 | 字段 | 说明 |
|---:|---:|---|---|
| 0-1 | 2 | Transaction ID | 与请求一致 |
| 2-3 | 2 | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | Length | 固定 `00 06` |
| 6 | 1 | Unit ID | 从站 ID |
| 7 | 1 | Function Code | `06` |
| 8-9 | 2 | Register Address | 被写入地址 |
| 10-11 | 2 | Register Value | 被写入值 |

## 8. 功能码 0x10：写多个保持寄存器

项目函数：

- `DeltaPLC2.WriteUShorts(ushort startAddress, ushort[] values)`
- `DeltaPLC2.WriteFloat(ushort startAddress, float value)`，一个 Float 写 2 个寄存器
- `DeltaPLC2.WriteFloats(ushort startAddress, float[] values)`，每个 Float 写 2 个寄存器

### 8.1 请求格式

总长度：

```text
13 + N * 2 字节
```

其中 `N` 为写入寄存器数量。

| 字节序号 | 长度 | 示例 | 字段 | 说明 |
|---:|---:|---|---|---|
| 0-1 | 2 | `00 03` | Transaction ID | 事务号 |
| 2-3 | 2 | `00 00` | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | `00 0B` | Length | `Unit ID + PDU` 长度，写 2 个寄存器时为 11 |
| 6 | 1 | `01` | Unit ID | 从站 ID |
| 7 | 1 | `10` | Function Code | 写多个保持寄存器 |
| 8-9 | 2 | `11 90` | Start Address | 起始地址 |
| 10-11 | 2 | `00 02` | Quantity | 写入寄存器数量 |
| 12 | 1 | `04` | Byte Count | 数据字节数，`N * 2` |
| 13... | 可变 | - | Register Values | 写入数据，每个寄存器 2 字节，高字节在前 |

示例：向 `D400` 写入一个 Float，占 2 个寄存器：

```text
TT TT 00 00 00 0B 01 10 11 90 00 02 04 HH LL HH LL
```

说明：

- `TT TT` 为 NModbus 生成的事务号。
- `HH LL HH LL` 为 Float 拆分后的两个 16 位寄存器值。
- 本项目中 `DeltaPLC2.WriteFloat()` 使用 `BitConverter.GetBytes(value)`，再按本机小端序拆成两个 `ushort`，然后交给 NModbus 写入。NModbus 在报文层把每个 `ushort` 按 Modbus 大端序发送。

### 8.2 正常响应格式

总长度固定 12 字节：

| 字节序号 | 长度 | 示例 | 字段 | 说明 |
|---:|---:|---|---|---|
| 0-1 | 2 | `00 03` | Transaction ID | 与请求一致 |
| 2-3 | 2 | `00 00` | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | `00 06` | Length | 后续长度固定 6 |
| 6 | 1 | `01` | Unit ID | 从站 ID |
| 7 | 1 | `10` | Function Code | 写多个保持寄存器 |
| 8-9 | 2 | `11 90` | Start Address | 起始地址 |
| 10-11 | 2 | `00 02` | Quantity | 已写入寄存器数量 |

## 9. 功能码 0x01：读线圈

项目函数：

- `DeltaPLC2.ReadBool(ushort address)`
- `DeltaPLC2.ReadBools(ushort startAddress, ushort count)`

### 9.1 请求格式

总长度固定 12 字节：

| 字节序号 | 长度 | 示例 | 字段 | 说明 |
|---:|---:|---|---|---|
| 0-1 | 2 | `00 04` | Transaction ID | 事务号 |
| 2-3 | 2 | `00 00` | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | `00 06` | Length | 后续长度固定 6 |
| 6 | 1 | `01` | Unit ID | 从站 ID |
| 7 | 1 | `01` | Function Code | 读线圈 |
| 8-9 | 2 | `08 01` | Start Address | 起始线圈地址，例如 `M1 -> 0x0801` |
| 10-11 | 2 | `00 50` | Quantity | 读取线圈数量，例如 80 |

### 9.2 正常响应格式

总长度：

```text
9 + ByteCount 字节
```

`ByteCount = ceil(线圈数量 / 8)`。

| 字节序号 | 长度 | 字段 | 说明 |
|---:|---:|---|---|
| 0-1 | 2 | Transaction ID | 与请求一致 |
| 2-3 | 2 | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | Length | `Unit ID + Function + ByteCount + Data` |
| 6 | 1 | Unit ID | 从站 ID |
| 7 | 1 | Function Code | `01` |
| 8 | 1 | Byte Count | 后续线圈状态字节数 |
| 9... | 可变 | Coil Status | 每个 bit 表示一个线圈，低位对应起始地址 |

例如读取 80 个线圈，`ByteCount = 10`，响应总长度为 19 字节。

## 10. 功能码 0x05：写单个线圈

项目函数：

- `DeltaPLC2.WriteBool(ushort address, bool value)`

### 10.1 请求格式

总长度固定 12 字节：

| 字节序号 | 长度 | 示例 | 字段 | 说明 |
|---:|---:|---|---|---|
| 0-1 | 2 | `00 05` | Transaction ID | 事务号 |
| 2-3 | 2 | `00 00` | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | `00 06` | Length | 后续长度固定 6 |
| 6 | 1 | `01` | Unit ID | 从站 ID |
| 7 | 1 | `05` | Function Code | 写单个线圈 |
| 8-9 | 2 | `05 04` | Coil Address | 线圈地址，例如 `Y4 -> 0x0504` |
| 10-11 | 2 | `FF 00` | Value | `FF 00` 表示 ON，`00 00` 表示 OFF |

### 10.2 正常响应格式

正常响应回显请求 PDU，总长度固定 12 字节。

## 11. 功能码 0x0F：写多个线圈

项目函数：

- `DeltaPLC2.WriteBools(ushort startAddress, bool[] values)`

### 11.1 请求格式

总长度：

```text
13 + ByteCount 字节
```

`ByteCount = ceil(线圈数量 / 8)`。

| 字节序号 | 长度 | 示例 | 字段 | 说明 |
|---:|---:|---|---|---|
| 0-1 | 2 | `00 06` | Transaction ID | 事务号 |
| 2-3 | 2 | `00 00` | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | 可变 | Length | `Unit ID + PDU` 长度 |
| 6 | 1 | `01` | Unit ID | 从站 ID |
| 7 | 1 | `0F` | Function Code | 写多个线圈 |
| 8-9 | 2 | `05 04` | Start Address | 起始线圈地址 |
| 10-11 | 2 | `00 04` | Quantity | 写入线圈数量 |
| 12 | 1 | `01` | Byte Count | 后续线圈数据字节数 |
| 13... | 可变 | - | Coil Values | 每个 bit 表示一个线圈，低位对应起始地址 |

### 11.2 正常响应格式

总长度固定 12 字节：

| 字节序号 | 长度 | 字段 | 说明 |
|---:|---:|---|---|
| 0-1 | 2 | Transaction ID | 与请求一致 |
| 2-3 | 2 | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | Length | 固定 `00 06` |
| 6 | 1 | Unit ID | 从站 ID |
| 7 | 1 | Function Code | `0F` |
| 8-9 | 2 | Start Address | 起始地址 |
| 10-11 | 2 | Quantity | 已写入线圈数量 |

## 12. 异常响应格式

当 PLC 返回异常响应时，功能码最高位会置 1：

```text
异常功能码 = 原功能码 + 0x80
```

例如：

- `0x03` 的异常响应功能码是 `0x83`
- `0x10` 的异常响应功能码是 `0x90`

异常响应总长度通常为 9 字节：

| 字节序号 | 长度 | 字段 | 说明 |
|---:|---:|---|---|
| 0-1 | 2 | Transaction ID | 与请求一致 |
| 2-3 | 2 | Protocol ID | 固定 `00 00` |
| 4-5 | 2 | Length | 通常 `00 03` |
| 6 | 1 | Unit ID | 从站 ID |
| 7 | 1 | Exception Function Code | 原功能码 + `0x80` |
| 8 | 1 | Exception Code | 异常码 |

常见异常码：

| 异常码 | 含义 |
|---:|---|
| `01` | 非法功能码 |
| `02` | 非法数据地址 |
| `03` | 非法数据值 |
| `04` | 从站设备故障 |

## 13. 项目当前读写点

### 13.1 周期读取

周期读取在 `Models/DataAqc.cs` 的 `DataAqc.Refresh(Dispatcher dispatcher)` 中。

主要读取：

| PLC 地址 | 类型 | 项目调用 |
|---|---|---|
| `D400` 起 | Float 数组 | `plc.ReadFloats(..., 16)` |
| `D410` | Word | `plc.ReadUShort(...)` |
| `D46` 起 | Float 数组 | `plc.ReadFloats(..., 11)` |
| `D249` | Float | `plc.ReadFloat(...)` |
| `D260` | Float | `plc.ReadFloat(...)` |
| `D362` 起 | Float 数组 | `plc.ReadFloats(..., 2)` |
| `M1` 起 | Bool 数组 | `plc.ReadBools(..., 80)` |
| `Y4` 起 | Bool 数组 | `plc.ReadBools(..., 4)` |

### 13.2 参数写入

参数写入在 `MainViewModel.cs` 中。

| 参数 | PLC 地址 | 类型 | 项目调用 |
|---|---|---|---|
| 冲程压边力设定 | `D400` | Float | `WriteFloat` |
| 闭环压边力设定 | `D402` | Float | `WriteFloat` |
| 速度设定 | `D404` | Float | `WriteFloat` |
| 拉伸位移上限 | `D412` | Float | `WriteFloat` |
| 停机比例设定 | `D416` | Float | `WriteFloat` |
| 停机延时设定 | `D410` | Word | `WriteUShort` |

### 13.3 线圈写入

线圈写入在 `MainViewModel.cs` 的 `WriteBoolVariableAsync(...)` 中，最终调用：

```csharp
DataAqc.plc.WriteBool(Address(variableName), value);
```

## 14. Float 数据格式

本项目的 Float 占用 2 个 16 位保持寄存器。

读取时：

```csharp
ushort[] registers = _master.ReadHoldingRegisters(_slaveId, startAddress, 2);
byte[] bytes = new byte[4];
BitConverter.GetBytes(registers[0]).CopyTo(bytes, 0);
BitConverter.GetBytes(registers[1]).CopyTo(bytes, 2);
return BitConverter.ToSingle(bytes, 0);
```

写入时：

```csharp
byte[] bytes = BitConverter.GetBytes(value);
ushort[] registers = new ushort[2];
registers[0] = BitConverter.ToUInt16(bytes, 0);
registers[1] = BitConverter.ToUInt16(bytes, 2);
_master.WriteMultipleRegisters(_slaveId, startAddress, registers);
```

也就是说，应用层把 .NET `float` 的 4 个字节拆成两个 `ushort`，再由 NModbus 按 Modbus 寄存器格式发送。若 PLC 端浮点字序与当前程序不一致，表现通常是 Float 读数异常，需要调整两个寄存器的字序或字节序。

## 15. 与旧 `DeltaPLC` 手工报文实现的关系

`Tools/DeltaPLC.cs` 中手工实现了部分 Modbus TCP 报文：

- `BuildReadRequest(...)`：功能码 `0x03`
- `BuildWriteSingleRequest(...)`：功能码 `0x06`
- `BuildWriteMultipleRequest(...)`：功能码 `0x10`

这些格式与本文档的 Modbus TCP 格式一致。但当前项目没有使用 `new DeltaPLC(...)`，所以实际通信以 `DeltaPLC2 + NModbus` 为准。
