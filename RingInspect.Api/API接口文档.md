# 圆环外缘缺陷检测 API 接口文档

> 版本 **1.0.0** ｜ .NET 8（`net8.0`）Minimal API ｜ 算法与网页版 `detect.js` V6 **逐字段一致**（实测 41/41 场景，见 `E:\Opencv\compare\实测对比报告.md`）

---

## 目录

1. [概览](#1-概览)
2. [启动与运行](#2-启动与运行)
3. [通用约定](#3-通用约定)
4. [接口详解](#4-接口详解)
   - [GET /](#41-get-)
   - [GET /api/health](#42-get-apihealth)
   - [GET /api/params](#43-get-apiparams)
   - [POST /api/detect/strict](#44-post-apidetectstrict默认参数--严格模式)
   - [POST /api/detect](#45-post-apidetect全参数)
   - [POST /api/detect/{key}](#46-post-apidetectkey单参数调控16-个)
   - [POST /api/detect/batch](#47-post-apidetectbatch批量)
   - [POST /api/debug/pixels](#48-post-apidebugpixels调试)
5. [参数详解（16 个）](#5-参数详解16-个)
6. [响应字段](#6-响应字段)
7. [错误码](#7-错误码)
8. [调用示例](#8-调用示例)
9. [性能与限制](#9-性能与限制)

---

## 1. 概览

| 项 | 值 |
|---|---|
| 基地址 | `http://127.0.0.1:60454` |
| 协议 | HTTP/1.1，JSON（UTF-8） |
| 图片上传 | **base64**（纯 base64 或 `data:image/png;base64,...` 均可） |
| 参数个数 | 16 个，**每个参数一个独立接口** |
| 默认预设 | 默认参数 + 严格模式 = `POST /api/detect/strict` |
| 算法来源 | 网页版 `E:\Opencv\缺陷检测网页程序\detect.js`（V6）逐行移植 |

接口清单：

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/` | 服务自述（接口目录） |
| GET | `/api/health` | 健康检查 |
| GET | `/api/params` | **参数文档**：16 个参数的默认值/范围/单位/提示/对应接口 |
| POST | `/api/detect/strict` | **默认参数 + 严格模式**（固定预设，不读 `params`） |
| POST | `/api/detect` | 全参数自定义（`params` 里想传几个传几个） |
| POST | `/api/detect/{key}` | **单参数调控**：只改一个参数，其余用默认值 |
| POST | `/api/detect/batch` | 批量检测（一次最多 64 张，共用同一组参数） |
| POST | `/api/debug/pixels` | 调试：返回解码后 RGBA 的 SHA-256（用于核对解码一致性） |

---

## 2. 启动与运行

```powershell
cd E:\Opencv\RingInspect.Api

# 开发运行
dotnet run

# 或先编译再运行
dotnet build -c Release
dotnet bin\Release\net8.0\RingInspect.Api.dll
```

- 默认监听 `http://127.0.0.1:60454`；用 `ASPNETCORE_URLS` 环境变量或 `--urls` 参数可改：

  ```powershell
  $env:ASPNETCORE_URLS='http://0.0.0.0:60454'; dotnet run
  # 或
  dotnet bin\Debug\net8.0\RingInspect.Api.dll --urls http://0.0.0.0:60454
  ```

- 依赖：**.NET 8** SDK/运行时 + `SixLabors.ImageSharp 2.1.3`（目标框架 `net8.0`，实测运行在 .NET 8.0.19；
  用 .NET 9 SDK 也能编译，`GET /api/health` 的 `runtime` 字段会显示实际运行时）。
  项目自带 `NuGet.config`（清空了远端源），包从本机全局缓存 `%USERPROFILE%\.nuget\packages` 解析，**离线可构建**。
- 请求体上限 128 MB，单图上限 4000 万像素。
- 已开启 CORS（任意来源），方便网页/前端直接调用。

启动后先验活：

```powershell
curl.exe http://127.0.0.1:60454/api/health
```

---

## 3. 通用约定

### 3.1 图片怎么传

所有检测接口都用 `image` 字段传 **base64 字符串**：

- 可以是纯 base64：`"iVBORw0KGgoAAAANSUhEUg..."`
- 也可以带 data URL 前缀：`"data:image/png;base64,iVBORw0KGgo..."`
- 自动忽略换行/空格，兼容 URL-safe 的 `-` `_`，缺少的 `=` 会自动补齐。
- 别名：`imageBase64`、`base64`（三者任选其一，`image` 优先）。
- 支持格式：PNG、JPEG、BMP、GIF、TIFF、WebP 等 ImageSharp 能识别的格式。
- 透明度：忽略 alpha 通道，灰度取 `max(r, g, b)`——与网页版完全一致。

### 3.2 角度、坐标与尺寸换算

- 角度单位是**度**，`0°` 在正右方，**顺时针**增加，`90°` 在正下方。
- 所有长度/面积参数（`minArea`、`minReach`、`notchDepth`、`pairDist`、`bigNotchDepth`、`gap`、`band`）
  都以**外半径 500 px** 为基准书写，检测时按实测外半径自动缩放：

  ```
  s = R / 500
  实际阈值 = 参数值 × s        （面积参数用 × s²）
  ```

  响应里的 `scale` 是 `s`，`scaled` 是换算后的实际阈值，便于核对。
- 这样同一组参数在 600×500 的小图和 2448×2048 的大图上都适用。

### 3.3 错误模型

分两类，注意区别：

| 情况 | HTTP | 响应体 |
|---|---|---|
| 请求格式错、参数越界、缺图片、批量超限 | **400 / 404** | `{"ok":false,"errorCode":"...","error":"..."}` |
| 图片能解码，但检测失败（找不到圆环、拟合失败） | **200** | `{"ok":false,"errorCode":"NO_RING","error":"..."}` |
| 图片解码失败（坏 base64、坏文件） | **200** | `{"ok":false,"errorCode":"DECODE_FAILED","error":"..."}` |

> 检测失败用 HTTP 200 是为了和网页版对齐：网页里这是“这张图的结果”，批量时要计入 `err` 而不是整个请求失败。

### 3.4 布尔参数

`strictPair`、`loneDebrisNG` 是开关，以下写法都接受：

```json
{ "strictPair": true }
{ "strictPair": 1 }
{ "strictPair": "true" }
```

---

## 4. 接口详解

### 4.1 GET /

服务自述，返回接口目录，便于快速确认版本。

```json
{
  "name": "圆环外缘缺陷检测 API",
  "version": "1.0.0",
  "note": "算法与网页版 detect.js V6 完全一致；图片以 base64 上传。",
  "endpoints": {
    "health": "GET /api/health",
    "paramDoc": "GET /api/params",
    "strictPreset": "POST /api/detect/strict",
    "custom": "POST /api/detect",
    "batch": "POST /api/detect/batch",
    "singleParam": "POST /api/detect/{参数名}（16 个参数各一个，见 GET /api/params）",
    "debugPixels": "POST /api/debug/pixels"
  }
}
```

### 4.2 GET /api/health

```json
{
  "ok": true,
  "service": "RingInspect.Api",
  "version": "1.0.0",
  "runtime": ".NET 8.0.19",
  "targetFramework": "net8.0",
  "serverTime": "2026-11-14 21:30:00 +08:00",
  "referenceRadius": 500,
  "profilePoints": 1440
}
```

`runtime` 是实际运行时版本，`targetFramework` 是编译目标，可用来确认部署的确实是 .NET 8。

`referenceRadius` = 参数基准半径，`profilePoints` = 极坐标剖线点数（1440，每 0.25°）。

### 4.3 GET /api/params

**参数文档接口**：返回 16 个参数的定义、默认值、范围、步长、单位、网页提示、所属分组，以及每个参数对应的接口路径。

```json
{
  "referenceRadius": 500,
  "profilePoints": 1440,
  "note": "所有长度/面积参数均以“外半径 500px”为基准，检测时按实测半径自动换算。",
  "defaults": {
    "darkTh": 128, "debrisTh": 230, "gap": 3, "band": 70,
    "minArea": 14, "minReach": 12, "minElong": 2.2,
    "notchDepth": 14, "notchMinWidth": 1, "baselineWin": 20,
    "pairDist": 40, "bigNotchDepth": 30,
    "loneDebrisNG": true, "strictPair": true,
    "angFrom": 0, "angTo": 360
  },
  "endpoints": { "...": "..." },
  "parameters": [
    {
      "key": "debrisTh",
      "label": "碎屑灰度阈值",
      "kind": "number",
      "defaultValue": 240,
      "min": 60,
      "max": 250,
      "step": 1,
      "unit": "",
      "hint": "外缘外侧比它暗的像素算可疑。越大越敏感，背景脏时会误报",
      "group": "basic",
      "endpoint": "/api/detect/debrisTh"
    }
  ]
}
```

### 4.4 POST /api/detect/strict（默认参数 + 严格模式）

**最常用的接口**：固定使用默认参数，并强制 `strictPair = true`。

- 请求里的 `params` 会被**忽略**（保证“严格模式预设”永远一致）。
- 想改任何一个参数，请用 `/api/detect` 或 `/api/detect/{key}`。

请求：

```json
{
  "image": "data:image/png;base64,iVBORw0KGgo...",
  "fileName": "part-001.png",
  "includeProfile": false,
  "includeOverlay": false
}
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `image` | string | 是 | 图片 base64（别名 `imageBase64` / `base64`） |
| `fileName` | string | 否 | 原样回显到结果里 |
| `includeProfile` | bool | 否 | 是否返回 1440 点极坐标剖线（体积较大，默认 false） |
| `includeOverlay` | bool | 否 | 是否返回叠加图 PNG 的 data URL（默认 false） |

响应（`case02` 缺口+尖刺，节选）：

```json
{
  "ok": true,
  "verdict": "NG",
  "timeMs": 83.4,
  "width": 1200,
  "height": 1000,
  "circle": { "cx": 604.4715158516758, "cy": 497.1893449843754, "R": 430.0625914093947 },
  "scale": 0.8601251828187895,
  "ring": { "area": 401630, "bbox": [173, 66, 1036, 929] },
  "band": { "gap": 2.5803755484563684, "band": 60.20876279731526, "pixels": 174924 },
  "params": {
    "darkTh": 128, "debrisTh": 230, "gap": 3, "band": 70,
    "minArea": 14, "minReach": 12, "minElong": 2.2,
    "notchDepth": 14, "notchMinWidth": 1, "baselineWin": 20,
    "pairDist": 40, "bigNotchDepth": 30,
    "loneDebrisNG": true, "strictPair": true,
    "angFrom": 0, "angTo": 360
  },
  "scaled": {
    "gap": 2.5803755484563684,
    "band": 60.20876279731526,
    "minArea": 10.357414621666784,
    "minReach": 10.321502193825474,
    "notchDepth": 12.041752559463053,
    "pairDist": 34.40500731275158
  },
  "defects": [
    {
      "id": 1,
      "type": "paired",
      "typeName": "缺口伴生尖刺/异物",
      "angle": 36.99733478958702,
      "bbox": [934.3742554476087, 726.4138255271434, 977, 779],
      "x": 955.6871277238043,
      "y": 752.7069127635717,
      "depthPx": 28.053571428571388,
      "widthDeg": 2.75,
      "arcLenPx": 20.6764574103963,
      "areaPx": 234,
      "sizePx": [33, 28],
      "minLum": 32,
      "reachPx": 43.10866971016935,
      "spikeCount": 1,
      "gapPx": 0,
      "notchBox": [934.3742554476087, 726.4138255271434, 962.659810108467, 751.9192728026723],
      "debrisBox": [944, 751, 977, 779],
      "debrisBoxes": [[944, 751, 977, 779]]
    }
  ],
  "candidates": [],
  "defectCount": 1,
  "filteredCount": 0
}
```

判定规则（与网页一致）：

- **严格模式**：只有“缺口 + 附近尖刺/异物”成对出现才判 `NG`；单独的尖刺、单独的缺口、大缺口都不判。
- **宽松模式**（`strictPair=false`）：尖刺/异物可单独判（受 `loneDebrisNG` 控制），缺口深度达到 `bigNotchDepth` 也可单独判。
- 没有 `defects` 就是 `OK`，`candidates` 是“看到了但被规则过滤掉”的可疑项，仅供排查。

### 4.5 POST /api/detect（全参数）

请求体在 `/api/detect/strict` 的基础上多一个 `params`，**只传想改的字段**，其余用默认值：

```json
{
  "image": "iVBORw0KGgo...",
  "params": {
    "debrisTh": 250,
    "band": 40,
    "strictPair": false
  }
}
```

- `params` 里的 key 必须是 16 个参数之一；未知 key 会被忽略。
- 数值超出范围 → `400 INVALID_PARAM`（范围见第 5 节）。
- 响应与 `/api/detect/strict` 相同，`params` 字段回显本次生效的全部 16 个参数。

### 4.6 POST /api/detect/{key}（单参数调控，16 个）

每个参数一个独立接口，**只改这一个参数，其余全部用默认值**：

```
POST /api/detect/debrisTh
POST /api/detect/minArea
POST /api/detect/minReach
POST /api/detect/minElong
POST /api/detect/notchDepth
POST /api/detect/notchMinWidth
POST /api/detect/pairDist
POST /api/detect/bigNotchDepth
POST /api/detect/darkTh
POST /api/detect/gap
POST /api/detect/band
POST /api/detect/baselineWin
POST /api/detect/angFrom
POST /api/detect/angTo
POST /api/detect/strictPair
POST /api/detect/loneDebrisNG
```

请求（`value` 必填，数字参数传 number，开关传 true/false 或 1/0）：

```json
{
  "image": "iVBORw0KGgo...",
  "fileName": "part-002.png",
  "value": 250
}
```

也支持把 `value` 放在查询串里，方便手测：

```
POST /api/detect/debrisTh?value=250
```

如果还想同时带别的参数（例如测 `loneDebrisNG` 时需要先关掉严格模式），用 `params` 补：

```json
{
  "image": "iVBORw0KGgo...",
  "value": false,
  "params": { "strictPair": false }
}
```

> `params` 在 `value` 之前生效，`value` 最后覆盖该接口对应的参数，所以不会被 `params` 里的同名值覆盖（也没必要重复传）。

响应比其它接口多一个 `applied`，明确告诉你哪个参数被设成了什么：

```json
{
  "ok": true,
  "verdict": "OK",
  "applied": { "key": "debrisTh", "kind": "number", "value": 250 },
  "...": "其余字段同 /api/detect/strict"
}
```

错误：

| 情况 | HTTP | errorCode |
|---|---|---|
| `{key}` 不是 16 个参数之一 | 404 | `UNKNOWN_PARAM` |
| 没传 `value` | 400 | `MISSING_VALUE` |
| `value` 类型不对 / 超出范围 | 400 | `INVALID_PARAM` |

### 4.7 POST /api/detect/batch（批量）

一次最多 **64 张**，所有图片共用同一组参数（`params` 可选）：

```json
{
  "params": { "strictPair": true },
  "includeProfile": false,
  "includeOverlay": false,
  "items": [
    { "fileName": "a.png", "image": "iVBORw0KGgo..." },
    { "fileName": "b.png", "image": "iVBORw0KGgo..." }
  ]
}
```

响应：

```json
{
  "total": 4,
  "ok": 1,
  "ng": 2,
  "err": 1,
  "elapsedMs": 265.945,
  "results": [ { "ok": true, "fileName": "a.png", "verdict": "OK", "...": "..." } ]
}
```

- `ok` = 判定 OK 的张数，`ng` = 判定 NG，`err` = 解码/检测失败。
- 单张失败不会中断整批，失败项在 `results` 里带 `ok:false` 和 `errorCode`。
- 超过 64 张 → `400 BATCH_TOO_LARGE`；`items` 为空 → `400 EMPTY_BATCH`。

### 4.8 POST /api/debug/pixels（调试）

返回解码后 RGBA 的 SHA-256，用来核对“API 解码的像素”与“浏览器 canvas 的像素”是否完全一致：

```json
{ "image": "iVBORw0KGgo..." }
```

```json
{
  "ok": true,
  "width": 1200,
  "height": 1000,
  "bytes": 4800000,
  "sha256": "3f1c..."
}
```

---

## 5. 参数详解（16 个）

| 参数 key | 界面名称 | 类型 | 默认值 | 取值范围 | 步长 | 单位 | 接口 |
|---|---|---|---|---|---|---|---|
| `debrisTh` | 碎屑灰度阈值 | 数字 | 230 | 60 ~ 250 | 1 | - | `POST /api/detect/debrisTh` |
| `minArea` | 碎屑最小面积 | 数字 | 14 | 2 ~ 300 | 1 | px² | `POST /api/detect/minArea` |
| `minReach` | 尖端最小伸出长度 | 数字 | 12 | 0 ~ 40 | 0.5 | px | `POST /api/detect/minReach` |
| `notchDepth` | 缺口最小深度 | 数字 | 14 | 3 ~ 60 | 0.5 | px | `POST /api/detect/notchDepth` |
| `notchMinWidth` | 缺口最小宽度 | 数字 | 1 | 0.25 ~ 10 | 0.25 | ° | `POST /api/detect/notchMinWidth` |
| `strictPair` | 严格模式 | 开关 | true | true / false | - | - | `POST /api/detect/strictPair` |
| `loneDebrisNG` | 独立尖刺/异物判缺陷 | 开关 | true | true / false | - | - | `POST /api/detect/loneDebrisNG` |
| `pairDist` | 缺口-异物最大间距 | 数字 | 40 | 5 ~ 120 | 1 | px | `POST /api/detect/pairDist` |
| `bigNotchDepth` | 大缺口独立判定深度 | 数字 | 30 | 0 ~ 80 | 1 | px | `POST /api/detect/bigNotchDepth` |
| `minElong` | 尖刺最小细长比 | 数字 | 2.2 | 1.2 ~ 5 | 0.1 | - | `POST /api/detect/minElong` |
| `darkTh` | 零件暗阈值 | 数字 | 128 | 40 ~ 220 | 1 | - | `POST /api/detect/darkTh` |
| `gap` | 检测带内边距 | 数字 | 3 | 2 ~ 60 | 0.5 | px | `POST /api/detect/gap` |
| `band` | 检测带宽度 | 数字 | 70 | 10 ~ 250 | 1 | px | `POST /api/detect/band` |
| `baselineWin` | 基线窗口 | 数字 | 20 | 3 ~ 40 | 1 | ° | `POST /api/detect/baselineWin` |
| `angFrom` | 只看角度 从 | 数字 | 0 | 0 ~ 360 | 1 | ° | `POST /api/detect/angFrom` |
| `angTo` | 只看角度 到 | 数字 | 360 | 0 ~ 360 | 1 | ° | `POST /api/detect/angTo` |

参数含义（与网页里的提示一致）：

| 参数 | 说明 |
|---|---|
| `debrisTh` | 外缘外侧比它暗的像素算可疑。越大越敏感，背景脏时会误报 |
| `minArea` | 碎屑最小面积，按外半径 500px 计，随图像尺寸自动换算 |
| `minReach` | 至少比外缘扎出多少 px 才算尖端/异物，用来滤掉贴在边缘的小齿尖和光晕 |
| `notchDepth` | 外缘比局部基线凹进多少 px 算缺口；严格/宽松两种模式都用它找缺口 |
| `notchMinWidth` | 缺口最小宽度（度），太窄的划痕不算 |
| `strictPair` | 开：只有“缺口+附近尖端”成对才 NG；关：尖端可单独判，缺口达到 `bigNotchDepth` 也可单独判 |
| `loneDebrisNG` | **仅宽松模式生效**；开启可检出无缺口的独立细丝/尖刺 |
| `pairDist` | 缺口在此距离（px）内有异物才配对 |
| `bigNotchDepth` | 缺口深度达到此值时，没有尖端也直接判缺陷；`0` = 关闭；严格模式下自动失效 |
| `minElong` | 连通块主轴长/宽至少达到此比值才算细长尖刺，用来滤除圆钝滚花齿 |
| `darkTh` | 比它暗的像素当作零件本体（找圆环用） |
| `gap` | 离外缘多远开始找碎屑，用来避开边缘齿纹和模糊 |
| `band` | 外缘向外多宽的范围内找碎屑/异物 |
| `baselineWin` | 外缘基线取前后这么多度的 65% 分位 |
| `angFrom` / `angTo` | 只检测这个角度范围；`angTo - angFrom >= 360` 表示整圈 |

**参数之间的联动**（和网页完全一致）：

- `strictPair = true` 时，`loneDebrisNG` 和 `bigNotchDepth` **都不生效**（严格模式只认配对）。
- `strictPair = false` 时，才轮到 `loneDebrisNG`、`bigNotchDepth` 起作用。
- `bigNotchDepth = 0` 表示关闭“大缺口独立判定”。

---

## 6. 响应字段

### 6.1 顶层 `DetectResult`

| 字段 | 类型 | 说明 |
|---|---|---|
| `ok` | bool | 检测是否成功执行（不代表判定结果） |
| `error` / `errorCode` | string | 失败时的原因/错误码 |
| `fileName` | string | 回显请求里的文件名 |
| `verdict` | string | `"OK"` / `"NG"` |
| `timeMs` | number | 检测耗时（毫秒，不含解码） |
| `width` / `height` | int | 图片尺寸 |
| `circle` | object | 拟合出的外圆：`cx`、`cy`、`R`（**大写 R**，与网页一致） |
| `scale` | number | 尺寸换算系数 `s = R / 500` |
| `ring` | object | 零件本体：`area`（像素数）、`bbox`（`[x0,y0,x1,y1]`） |
| `band` | object | 检测带：`gap`、`band`（换算后 px）、`pixels`（带内像素数） |
| `params` | object | 本次生效的 16 个参数 |
| `scaled` | object | 换算后的实际阈值：`gap`、`band`、`minArea`、`minReach`、`notchDepth`、`pairDist` |
| `defects` | array | 判定为缺陷的条目（见 6.2） |
| `candidates` | array | 被规则过滤掉的可疑项（字段同 defects，无 `id`） |
| `defectCount` | int | `defects.length` |
| `filteredCount` | int | `candidates.length` |
| `profile` | object | 仅 `includeProfile:true` 时返回，见 6.3 |
| `overlay` | string | 仅 `includeOverlay:true` 时返回，`data:image/png;base64,...` |
| `applied` | object | 仅单参数接口返回：`{key, kind, value}` |

### 6.2 `defects[]` / `candidates[]`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | int | 缺陷序号（按角度从小到大 1..n；candidates 没有） |
| `type` | string | `paired`（缺口+尖刺配对）/ `bignotch`（大缺口）/ `notch`（缺口候选）/ `spike` / `debris` / `burr` |
| `typeName` | string | 中文类型名 |
| `angle` | number | 缺陷中心角度（度） |
| `bbox` | number[4] | 包围盒 `[x0,y0,x1,y1]`（像素，浮点） |
| `x` / `y` | number | 包围盒中心（等价于网页导出时的 `x`/`y`） |
| `depthPx` | number | 缺口深度（像素） |
| `depthNorm` | number | 缺口深度 / 基线（归一化） |
| `widthDeg` | number | 缺口宽度（度） |
| `arcLenPx` | number | 缺口弧长（像素） |
| `angFrom` / `angTo` | number | 缺口起止角度 |
| `areaPx` | int | 异物/尖刺面积（像素数） |
| `sizePx` | int[2] | 异物包围盒宽高 |
| `minLum` | int | 异物最暗灰度 |
| `reachPx` | number | 尖端伸出外缘的长度（像素） |
| `elong` | number | 细长比（主轴长/短轴） |
| `pcaLen` | number | 主轴长度 |
| `freePts` | int | 自由端点数 |
| `spikeCount` | int | 配对时合并的尖刺个数 |
| `gapPx` | number | 缺口到异物的最近距离 |
| `notchBox` | number[4] | 缺口局部框（叠加图里的橙色虚线框） |
| `debrisBox` | number[4] | 异物框（粉色虚线框） |
| `debrisBoxes` | number[4][] | 配对时每个异物的框 |

> 不同 `type` 只会出现对应的字段，其余字段为 `null`（JSON 里直接省略）。

### 6.3 `profile`（`includeProfile: true`）

| 字段 | 说明 |
|---|---|
| `n` | 1440（每 0.25° 一个点） |
| `r[]` | 每个角度检出的外缘半径 |
| `rBody[]` | 零件本体在该角度的半径 |
| `base[]` | 局部基线（65% 分位） |
| `dev[]` | `base - r`，正数表示凹进（缺口） |
| `sigma` | 噪声估计 |

---

## 7. 错误码

| errorCode | HTTP | 含义 | 处理建议 |
|---|---|---|---|
| `MISSING_IMAGE` | 400 | 没传图片 | 检查 `image` 字段 |
| `DECODE_FAILED` | 200 | base64 或图片文件坏了 | 检查图片；`error` 里有细节 |
| `INVALID_PARAM` | 400 | 参数越界/类型不对 | 对照第 5 节范围 |
| `UNKNOWN_PARAM` | 404 | 路径里的参数名不存在 | 用 `GET /api/params` 查可用 key |
| `MISSING_VALUE` | 400 | 单参数接口没传 `value` | 补 `value` |
| `EMPTY_BATCH` | 400 | `items` 为空 | 至少传 1 张 |
| `BATCH_TOO_LARGE` | 400 | 超过 64 张 | 拆批 |
| `NO_RING` | 200 | 没找到圆环零件 | 调 `darkTh`，或检查图片 |
| `FIT_FAILED` | 200 | 圆拟合失败 | 检查图片是否被裁切/遮挡 |
| `INTERNAL_ERROR` | 500 | 未捕获异常 | 看服务端日志 |

`NO_RING` 的完整提示与网页版一字不差：

```
没找到圆环零件：需要一个不贴图像边缘、接近圆形的大块暗区。可尝试调整“零件暗阈值”。
```

---

## 8. 调用示例

### 8.1 curl（Windows 自带 curl.exe）

```bat
:: 先把图片转成 base64 存到文件，避免命令行过长
certutil -encodehex -f part.png part.b64 4 >nul
:: 或者用 PowerShell 生成纯 base64：
:: [Convert]::ToBase64String([IO.File]::ReadAllBytes('part.png')) > part.b64

curl.exe -s -X POST http://127.0.0.1:60454/api/detect/strict ^
  -H "Content-Type: application/json" ^
  --data-binary "@body.json"
```

`body.json`：

```json
{ "image": "iVBORw0KGgo...", "fileName": "part.png" }
```

单参数：

```bat
curl.exe -s -X POST "http://127.0.0.1:60454/api/detect/debrisTh?value=250" ^
  -H "Content-Type: application/json" ^
  --data-binary "@body.json"
```

### 8.2 PowerShell

```powershell
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes('E:\img\part.png'))
$body = @{ image = $b64; fileName = 'part.png' } | ConvertTo-Json -Compress

# 默认 + 严格模式
$r = Invoke-RestMethod -Uri 'http://127.0.0.1:60454/api/detect/strict' `
     -Method Post -ContentType 'application/json' -Body $body
"$($r.verdict)  缺陷 $($r.defectCount) 个  用时 $([math]::Round($r.timeMs,1)) ms"

# 单参数：只改缺口深度阈值
$body2 = @{ image = $b64; value = 30 } | ConvertTo-Json -Compress
$r2 = Invoke-RestMethod -Uri 'http://127.0.0.1:60454/api/detect/notchDepth' `
      -Method Post -ContentType 'application/json' -Body $body2
```

### 8.3 Python

```python
import base64, json, urllib.request

def detect(path, endpoint="/api/detect/strict", **extra):
    body = {"image": base64.b64encode(open(path, "rb").read()).decode(), **extra}
    req = urllib.request.Request(
        "http://127.0.0.1:60454" + endpoint,
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json"},
    )
    return json.load(urllib.request.urlopen(req))

r = detect("part.png")
print(r["verdict"], r["defectCount"], r["defects"])

# 只改一个参数
r = detect("part.png", "/api/detect/debrisTh", value=250)
print(r["applied"], r["verdict"])
```

### 8.4 C#

```csharp
using System.Net.Http.Json;

var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:60454") };

var body = new
{
    image = Convert.ToBase64String(await File.ReadAllBytesAsync(@"E:\img\part.png")),
    fileName = "part.png",
    includeOverlay = true,
};

var res = await http.PostAsJsonAsync("/api/detect/strict", body);
var json = await res.Content.ReadAsStringAsync();   // 直接拿 JSON 原文
```

---

## 9. 性能与限制

### 9.1 服务端检测耗时（响应里的 `timeMs`，不含解码）

| 图片尺寸 | 检测耗时 |
|---|---|
| 600 × 500 | 约 23 ms |
| 1200 × 1000 | 约 56 ms |
| 1800 × 1500 | 约 114 ms |
| 2448 × 2048 | 约 193 ms |

### 9.2 C# 客户端端到端耗时（实测）

用 `E:\Opencv\compare\bench`（`HttpClient`，本机 loopback、keep-alive、预热 3 次后跑 20 次取中位数，
目标框架同样是 `net8.0`）测得：

| 图片 | base64 载荷 | ① 读文件+base64+JSON | ② 上传+PNG 解码 | ③ 检测（服务端 `timeMs`） | ④ 响应+反序列化 | 端到端 ①+②+③+④ |
|---|---|---|---|---|---|---|
| 600 × 500 | 266 KB | 1.5 ms | 6.0 ms | 22.8 ms | 0.2 ms | **30.5 ms** |
| 1200 × 1000 | 971 KB | 3.7 ms | 17.8 ms | 55.5 ms | 0.1 ms | **75.2 ms** |
| 1800 × 1500 | 2.1 MB | 7.2 ms | 39.3 ms | 114.1 ms | 0.1 ms | **157.7 ms** |
| 2448 × 2048 | 3.9 MB | 7.8 ms | 71.9 ms | 192.3 ms | 0.0 ms | **263.1 ms** |

- ②“上传+解码”是把同一张图 POST 到 `/api/debug/pixels`（只解码、不检测）测出来的。
- 空请求基线（`GET /api/health`）中位数 **0.19 ms**，框架与网络开销可以忽略。
- 冷连接（每次新建 `HttpClient`/TCP）77.6 ms vs keep-alive 75.2 ms，本机只差约 2 ms。
- 结论：**时间几乎全花在服务端的 PNG 解码 + 检测上**（检测占 73~75%），与像素数近似线性；
  客户端 base64 编码和 JSON 解析都在 10 ms 以内。

复现：

```powershell
cd E:\Opencv\compare\bench
dotnet run -c Release -- http://127.0.0.1:60454 20              # 检测端到端
dotnet run -c Release -- http://127.0.0.1:60454 20 --decode     # 只上传+解码
```

原始输出见 `E:\Opencv\compare\out\bench_csharp.txt`。

### 9.3 限制

- 单图 ≤ 4000 万像素（约 6300×6300），请求体 ≤ 128 MB。
- 批量接口一次 ≤ 64 张，串行处理；`includeProfile` / `includeOverlay` 会让响应明显变大
  （实测：同一次检测，不带剖线 2.3 KB，带剖线 107 KB，带 overlay 约 1.2 MB），批量时建议关闭。
- 检测是 CPU 密集型，多实例部署时注意每实例的并发数。
