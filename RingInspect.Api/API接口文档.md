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
5. [参数详解（17 个）](#5-参数详解17-个)
6. [响应字段](#6-响应字段)
7. [错误码](#7-错误码)
8. [调用示例](#8-调用示例)
9. [性能与限制](#9-性能与限制)（含 20-40MB 大图实测与优化）

---

## 1. 概览

| 项 | 值 |
|---|---|
| 基地址 | `http://127.0.0.1:60454` |
| 协议 | HTTP/1.1，JSON（UTF-8） |
| 图片上传 | **base64**（纯 base64 或 `data:image/png;base64,...` 均可） |
| 参数个数 | 17 个，**每个参数一个独立接口** |
| 默认预设 | 默认参数 + 严格模式 = `POST /api/detect/strict` |
| 算法来源 | 网页版 `E:\Opencv\缺陷检测网页程序\detect.js`（V6）逐行移植 |

接口清单：

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/` | 服务自述（接口目录） |
| GET | `/api/health` | 健康检查 |
| GET | `/api/params` | **参数文档**：17 个参数的默认值/范围/单位/提示/对应接口 |
| POST | `/api/detect/strict` | **默认参数 + 严格模式**（固定预设，不读 `params`） |
| POST | `/api/detect` | 全参数自定义（`params` 里想传几个传几个） |
| POST | `/api/detect/{key}` | **单参数调控**：只改一个参数，其余用默认值 |
| POST | `/api/detect/batch` | 批量检测（一次最多 64 张，共用同一组参数） |
| POST | `/api/detect/path` | **只传本地路径**：服务端自己读文件解码，**20-40MB 大图最快** |
| POST | `/api/detect/path/strict` | 本地路径 + 默认严格预设 |
| POST | `/api/detect/path/batch` | 多个本地路径，批量检测 |
| POST | `/api/detect/blob` | 图片原始字节直接放 body（不做 base64） |
| POST | `/api/detect/blob/strict` | 原始字节 + 默认严格预设 |
| GET/POST | `/api/detect/fast` | **极速接口**：只要图片路径，固定严格预设，响应最精简 |
| GET/POST | `/api/switch` | 检测启停开关（与桌面界面共用同一状态） |
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
- **不想走 base64** 时有两个更快的入口：`POST /api/detect/blob`（原始字节放 body）、
  `POST /api/detect/path`（只传本地路径，服务端自己读文件）。大图（20-40MB）建议用路径接口。
- 响应里的 `baseImage`（JPEG 底图）**默认不返回**；需要时加 `"includeBaseImage": true`。
  不带底图时响应只有 2~3 KB，便宜很多。
- 所有检测接口都会做启停开关检查：开关关闭时统一返回 HTTP 409 + `DETECTION_PAUSED`。

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
    "singleParam": "POST /api/detect/{参数名}（17 个参数各一个，见 GET /api/params）",
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

**参数文档接口**：返回 17 个参数的定义、默认值、范围、步长、单位、网页提示、所属分组，以及每个参数对应的接口路径。

```json
{
  "referenceRadius": 500,
  "profilePoints": 1440,
  "note": "所有长度/面积参数均以“外半径 500px”为基准，检测时按实测半径自动换算。",
  "defaults": {
    "darkTh": 128, "debrisTh": 240, "gap": 3, "band": 70,
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
    "darkTh": 128, "debrisTh": 240, "gap": 3, "band": 70,
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

- `params` 里的 key 必须是 17 个参数之一；未知 key 会被忽略。
- 数值超出范围 → `400 INVALID_PARAM`（范围见第 5 节）。
- 响应与 `/api/detect/strict` 相同，`params` 字段回显本次生效的全部 17 个参数。

### 4.6 POST /api/detect/{key}（单参数调控，17 个）

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
| `{key}` 不是 17 个参数之一 | 404 | `UNKNOWN_PARAM` |
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

### 4.9 GET / POST /api/switch（检测启停开关）

和桌面（WPF）界面上的开关是**同一份状态**（同进程单例），网页/脚本也能改。

```powershell
curl.exe http://127.0.0.1:60454/api/switch
# {"ok":true,"enabled":true,"state":"running"}

curl.exe -X POST http://127.0.0.1:60454/api/switch ^
  -H "Content-Type: application/json" -d "{\"enabled\":false}"
# {"ok":true,"enabled":false,"state":"paused"}
```

- `enabled` 省略时表示**取反**当前状态。
- 关闭后所有检测接口返回 HTTP **409** + `DETECTION_PAUSED`（`/api/health` 的 `detectionEnabled` 也会变 false）。

### 4.10 POST /api/detect/path · /api/detect/blob（不走 base64）

**路径接口**（服务端自己读文件，最省时间，适合 20-40MB 的大图）：

```json
POST /api/detect/path/strict
{
  "path": "图集1\\相机_20261106_145813_578_000047.bmp",
  "includeProfile": false,
  "includeOverlay": false,
  "includeBaseImage": false
}
```

- `path`（别名 `file`）可以是绝对路径，也可以是相对环境变量 `RINGINSPECT_ROOT` 的相对路径；
  未配置 `RINGINSPECT_ROOT` 时路径接口不可用（防目录穿越），返回 `BAD_PATH`。
- 批量：`POST /api/detect/path/batch`，body 里用 `"paths": ["a.bmp", "b.bmp"]`（≤ 64 张）。

**原始字节接口**：`POST /api/detect/blob[/strict]?includeProfile=false&includeOverlay=false&includeBaseImage=false&strict=true`，
body 直接放图片原始字节（`Content-Type: application/octet-stream`），参数走 query string。

### 4.11 GET/POST /api/detect/fast（极速接口，只要路径）

最省事也最快的单图接口：**只传图片路径**，固定用严格预设（17 个参数与 `/api/detect/path/strict` 完全相同），
不返回剖线/底图，响应结构与 `/api/detect/path/strict`（`includeProfile=false`、`includeOverlay=false`）**逐字段一致**。

```bash
# GET：路径放 query（中文要 URL 编码）
curl "http://127.0.0.1:5080/api/detect/fast?path=%E5%9B%BE%E9%9B%863%2F%E7%9B%B8%E6%9C%BA_20261116_171144_400_000033.bmp"

# POST：JSON 里只放 path（别名 file / imagePath 也可以）
curl -X POST http://127.0.0.1:5080/api/detect/fast -H "Content-Type: application/json" -d "{\"path\":\"图集3/相机_20261116_171144_400_000033.bmp\"}"
```

- 路径规则与 `/api/detect/path` 一致：绝对路径，或相对 `RINGINSPECT_ROOT` 的路径；未配置根目录返回 `BAD_PATH`。
- 检测开关关闭时返回 `409 DETECTION_PAUSED`；读盘失败 `400 READ_FAILED`；解码失败 `200 {"ok":false,"errorCode":"DECODE_FAILED"}`。
- 与 `/api/detect/path/strict` 的关系：**检测结果逐字段相同**（342 张图全量比对 0 差异），
  只是请求体更小（只有一个路径）+ 读文件用未清零直读，往返再省 5~10%。

联机（桌面版接上位机）时还有几条行为：

- **不给 path**：宿主配了「默认文件夹 + 默认文件名」就自动检测那个文件（适合上位机原地覆盖同一张图）；
  独立部署的 API 没配则返回 `400 BAD_PATH`。
- **自动重试**：读盘 / 解码失败会连续重试 3 次（间隔 30 ms），并核对读取前后的文件长度与修改时间，
  避免把"写了一半的图"当成品检；重试次数用环境变量 `RINGINSPECT_FAST_RETRY` 调。
- **联机武装（NOT_ARMED）**：宿主收到上位机 `start` 之前，本接口返回 `409 NOT_ARMED`；收到 `end` 之后继续拒绝，
  直到下一次 `start`。独立部署时不启用这一层（没有宿主）。
- **根目录限制**：桌面版内嵌运行时放开（`ApiEndpoints.AllowAnyPath = true`，本机可信），
  独立部署仍受 `RINGINSPECT_ROOT` 约束；宿主也可以编程设置 `ApiEndpoints.RootOverride` 指定根目录。
- **桌面端内部调用**：桌面版界面里选「走接口」时，请求带 `X-RingInspect-Internal: 1`，此时即使上位机还没发 `start`
  也不返回 409（本机自己批量检测不受联机武装限制）；外部调用带不带这个头都不影响原有逻辑。
- **读文件**：`ImageCodec.ReadAllBytesFast`（未清零直读 + 放开共享读写）也对外开放，
  桌面端 / 宿主直接用它读图，省掉「读文件 → base64 → 再解回字节」的两次大块搬运。

---

## 5. 参数详解（17 个）

| 参数 key | 界面名称 | 类型 | 默认值 | 取值范围 | 步长 | 单位 | 接口 |
|---|---|---|---|---|---|---|---|
| `debrisTh` | 碎屑灰度阈值 | 数字 | 240 | 60 ~ 250 | 1 | - | `POST /api/detect/debrisTh` |
| `minArea` | 碎屑最小面积 | 数字 | 14 | 2 ~ 300 | 1 | px² | `POST /api/detect/minArea` |
| `minReach` | 尖端最小伸出长度 | 数字 | 12 | 0 ~ 40 | 0.5 | px | `POST /api/detect/minReach` |
| `notchDepth` | 缺口最小深度 | 数字 | 14 | 3 ~ 60 | 0.5 | px | `POST /api/detect/notchDepth` |
| `notchMinWidth` | 缺口最小宽度 | 数字 | 1 | 0.25 ~ 10 | 0.25 | ° | `POST /api/detect/notchMinWidth` |
| `strictPair` | 严格模式 | 开关 | true | true / false | - | - | `POST /api/detect/strictPair` |
| `loneDebrisNG` | 独立尖刺/异物判缺陷 | 开关 | true | true / false | - | - | `POST /api/detect/loneDebrisNG` |
| `pairDist` | 缺口-异物最大间距 | 数字 | 40 | 5 ~ 120 | 1 | px | `POST /api/detect/pairDist` |
| `bigNotchDepth` | 大缺口独立判定深度 | 数字 | 30 | 0 ~ 80 | 1 | px | `POST /api/detect/bigNotchDepth` |
| `breakMinWidth` | 断裂最小弧长 | 数字 | 11 | 2 ~ 200 | 1 | px | `POST /api/detect/breakMinWidth` |
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
| `breakMinWidth` | **断口弧长短于此值就当作零件自带的开口（细缝），不判断裂**；按外半径 500px 计。零件自带开口一般 15px 以内，真断口一般 ≥ 20px，默认 11 兼顾两类图集 |
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
- `breakMinWidth` 只影响“整条径向都没有材料”的断口；缺口/尖刺判定不受影响。
  **断裂与缺口的分界**：某个角度上从外缘往里 3px 内都没有材料 → 断口；缺口只是外缘凹进去，
  凹口下面仍有材料 → 仍按缺口/配对判定。

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
| `params` | object | 本次生效的 17 个参数 |
| `scaled` | object | 换算后的实际阈值：`gap`、`band`、`minArea`、`minReach`、`notchDepth`、`pairDist` |
| `defects` | array | 判定为缺陷的条目（见 6.2） |
| `candidates` | array | 被规则过滤掉的可疑项（字段同 defects，无 `id`） |
| `defectCount` | int | `defects.length` |
| `filteredCount` | int | `candidates.length` |
| `profile` | object | 仅 `includeProfile:true` 时返回，见 6.3 |
| `overlay` | string | 仅 `includeOverlay:true` 时返回，`data:image/png;base64,...` |
| `baseImage` | string | 仅 `includeBaseImage:true` 时返回，全尺寸 JPEG data URL（默认不返回） |
| `timing` | object | 耗时明细（毫秒）：`readMs` 读文件（blob 模式为收取请求体）、`decodeMs` 解码、`detectMs` 检测、`encodeMs` 底图编码、`totalMs` 合计、`fileBytes` 文件字节数；另含检测内部阶段 `lumMs` 亮度、`clearMs` 缓冲清零、`fillMs` 连通域填充、`scanMs` 径向扫描、`bandMs` 碎屑带+配对、`components` 连通域个数 |
| `applied` | object | 仅单参数接口返回：`{key, kind, value}` |

### 6.2 `defects[]` / `candidates[]`

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | int | 缺陷序号（按角度从小到大 1..n；candidates 没有） |
| `type` | string | `paired`（缺口+尖刺配对）/ `bignotch`（大缺口）/ `broken`（**圆环断裂**）/ `notch`（缺口候选）/ `spike` / `debris` / `burr` |
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

`broken`（圆环断裂）的字段：

| 字段 | 说明 |
|---|---|
| `angle` | 断口中心角度（度） |
| `widthDeg` | 断口宽度（度） |
| `arcLenPx` | 断口弧长（像素，按实测半径） |
| `angFrom` / `angTo` | 断口起止角度 |
| `bbox` / `x` / `y` | 包围盒与中心 |
| `depthPx` / `depthNorm` | 恒为 0（断裂是整条径向都没材料，没有“深度”一说） |

**断环（环体被切成几段圆弧）**：拟合时会先把几段“共圆”的圆弧拼回同一个环，
因此断环不再返回 `NO_RING`，而是正常出结果并把每个断口报成 `broken` 缺陷（判定 NG）。

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
| `BAD_PATH` | 400 | 路径为空/越界/不存在（路径接口） | 配好 `RINGINSPECT_ROOT`，传合法路径 |
| `READ_FAILED` | 400 | 路径正确但读文件失败（被占用/无权限） | 检查文件权限 |
| `DETECTION_PAUSED` | 409 | 检测开关已关闭 | `POST /api/switch {"enabled":true}` 或打开桌面界面开关 |
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

### 9.1 本地路径接口实测（`POST /api/detect/path/strict`，本机 loopback，2026-11-16）

整集跑完取中位数（客户端只发一个 JSON 路径，不传图）：

| 图集 | 张数 | 尺寸 | 文件 | 往返中位 | p95 | 其中：读文件 | 解码 | 检测 |
|---|---|---|---|---|---|---|---|---|
| 图集1 | 88 | 2448 × 2048 | 15.0 MB | **65.5 ms** | 87.3 ms | 3 ms | 6 ms | 50 ms |
| 图集2 | 216 | 2448 × 2048 | 15.0 MB | **64.2 ms** | 73.2 ms | 3 ms | 6 ms | 49 ms |
| 图集3 | 38 | 4024 × 3036 | **36.7 MB** | **142.9 ms** | 159.0 ms | 9 ms | 12 ms | 101 ms |

图集3（12.2 MP）检测内部拆分：亮度图 18 ms + 连通域填充 57 ms + 径向扫描 11 ms + 碎屑带/配对 12 ms。

> 2026-11-16 第三轮优化（连通域换成行程+并查集）后：图集1 **57.5 ms**、图集2 **43.0 ms**、
> 图集3 **99.0 ms**，检测内部拆分变成亮度 16 + 填充 13 + 径向扫描 21 + 碎屑带 14 ms，详见 9.5。
响应体不含底图时只有 **2~3 KB**（`includeBaseImage:true` 会变成几百 KB ~ 1 MB）。

### 9.2 20-40MB 大图优化（精度零损失）

**不改格式、不转码、不缩放**：BMP / PNG / JPEG 只是解码方式不同，解码后一律按原始像素检测，
检测前**不会把图转成 JPEG**，也不会重采样。实测同一张 12.2MP 图：
BMP 原图 vs PNG 无损 → 拟合半径、缺陷完全一致（R 差 0.000 px）；
转成 JPEG(q=92) → 缺陷深度 68.78 → 68.19 px、圆心偏移 0.05 px（JPEG 的画质扰动会改判据，所以不能用）。
响应里唯一出现 JPEG 的地方是可选的 aseImage（只给前端画框当底图，默认不返回，永不参与检测）。

**没有做任何缩放/降采样**——所有判断仍然逐像素在原图上做（`max(r,g,b)` 灰度、4 邻域连通域、
每 0.25° 一条射线），因此判定结果与优化前**逐字段一致**（图集1 88 张、图集2 216 张 0 差异；
图集3 仅 6 张断环图由 `NO_RING` 变为 NG，属于本次新增的断裂检测）。

做的都是“不改变数值”的工程优化：

| 优化 | 说明 | 效果 |
|---|---|---|
| 底图默认不返回 | `baseImage` 改为 `includeBaseImage:true` 才生成 | 大图省 30~60 ms + 几百 KB~1MB 传输 |
| 缓冲区池化 | `lum/debLum/lab/stack/hit/seen` 改用 `ArrayPool` 复用，不再每张图向 LOH 申请 ~150 MB | 消除 Gen2 停顿 |
| 扫描线连通域填充 | 整行区间入栈代替逐像素 DFS，去掉每像素两次除法；阈值提到本地变量 | 12.2 MP：95 ms → 57 ms |
| 径向扫描并行 | 1440 条射线互不共享写入，改 `Parallel.For` | 36 ms → 11 ms |
| 服务端 GC | `ServerGarbageCollection` + `ConcurrentGarbageCollection` | 减少大图停顿 |

优化前后对比（同一台机器、同一批图、`POST /api/detect/path/strict`）：

| 图集 | 优化前 中位 | 优化后 中位 | 提升 |
|---|---|---|---|
| 图集1（15.0 MB） | 116.8 ms | **65.5 ms** | 1.8× |
| 图集2（15.0 MB） | 111.3 ms | **64.2 ms** | 1.7× |
| 图集3（36.7 MB） | 234.8 ms | **142.9 ms** | 1.6× |

### 9.3 C# 客户端端到端耗时（base64 模式，优化前的历史数据，仅供对比）

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

### 9.4 blob 模式（body 直传）为什么比路径慢，以及 2026-11-16 的二次优化

**现象**：同样一张 35 MB（4024 × 3036）的图，`/api/detect/path/strict` 往返 130~145 ms，
`/api/detect/blob/strict` 要 190~225 ms。差的这 60~80 ms 不是算法，而是“图要先进服务器”。

| 环节 | path 模式 | blob 模式 | 说明 |
|---|---|---|---|
| 图怎么进来 | 服务端 `File.ReadAllBytes` **7 ms** | 走 HTTP 收 body **38 ms**（python 客户端）/ **78 ms**（Chrome 页面） | 35 MB 必须经过网络栈与浏览器上传通道，物理成本 |
| 解码 | 7 ms | 7 ms | 相同 |
| 检测 | 118 ms | 118~141 ms | 相同（blob 多一份 35 MB body 常驻，GC 压力略高） |
| 合计 | **~132 ms** | **~190 ms** | 差值 ≈ 上传 |

**本次做的三件事（都不动一个像素的判定）**：

| 优化 | 改前 | 改后 | 效果（35 MB 图） |
|---|---|---|---|
| 收 body 直接读进目标数组 | `new MemoryStream()` + `CopyToAsync` + `ToArray()`：扩容复制 + 尾部复制，35 MB 图多拷约 **110 MB** | 按 `Content-Length` 一次分配、循环 `ReadAsync` 读满；未清零分配 | 收 body **47.0 → 38.3 ms**（14 MB 图 15.2 → 13.4 ms） |
| BMP 24/32bpp 直解 | ImageSharp `Identify` + `Load<Rgba32>`：多一份 49 MB 中间位图再拷一遍 | 自己解析头、BGR(A)→RGBA 直接写进目标数组，按行分块 `Parallel.For`，未清零分配 | 解码 **11.6 → 7.8 ms**（14 MB 图 5.4 → 3.2 ms） |
| 其余格式 | — | 不满足 24/32bpp + BI_RGB 就原样交给 ImageSharp（PNG/JPEG/RLE/压缩 BMP 行为不变） | 无回归 |

`/api/detect/path/strict` 同步受益（解码同样走快速通道）：

| 图集 | 优化前中位 | 优化后中位 | 提升 |
|---|---|---|---|
| 图集1（88 张，15.0 MB） | 71.2 ms | **75.5 ms** | 持平（小图解码只占 6 ms，波动为主） |
| 图集2（216 张，15.0 MB） | 70.4 ms | **64.4 ms** | 1.09× |
| 图集3（38 张，36.7 MB） | 149.5 ms | **126.8 ms** | 1.18× |

**精度验证（关键）**：

- 解码器逐字节比对：`VerifyDecode` 把 `ImageCodec` 快速通道的 RGBA 与 ImageSharp 通用解码的 RGBA 各算一次 SHA-256，
  14 个用例全部一致——24bpp / 32bpp、bottom-up / top-down、奇数宽度（行 4 字节对齐）、1×1、
  32bpp alpha 全 0 / 单个 1 / 全 0xFF，以及 4 张实拍图（2448×2048、4024×3036）。
- 判定回归：图集1 88 张、图集2 216 张 **0 差异**；图集3 仅 6 张断环图 `NO_RING → NG`（与新增断裂检测一致）。
- 复现：`E:\Opencv\compare\VerifyDecode\bin\Release\net8.0\VerifyDecode.exe <图片...>`，
  `--bench` 参数可对比两种解码耗时；`E:\Opencv\compare\blob_bench.py` 可复测 blob/path 延迟。

**调试开关（默认关闭，用于 A/B 对比）**：

```powershell
$env:RINGINSPECT_NO_FASTDECODE='1'   # 退回 ImageSharp 通用解码
$env:RINGINSPECT_OLD_BODYREAD='1'    # 退回 MemoryStream + CopyToAsync 的老写法
```

**还能再快的地方**（未做，按收益排序）：

1. **连通域填充**：12.2 MP 图里占 86 ms（检测总耗时 110~140 ms 的大头），目前单线程；
   改成并行连通域标记可望降到 25~30 ms，整体再快 25~30%。属于算法级改动，需要逐字节回归。
2. **body 缓冲池化**：35 MB 数组走 `ArrayPool` 复用，省掉每次 LOH 分配（约 2~3 ms + GC 压力）。
3. **不做**：把图压成 JPEG/PNG 再传。JPEG 会改判据（深度 68.78 → 68.19 px）；PNG 无损但解码慢 6 倍
   （实测 PNG 64 ms vs BMP 33 ms），省下的上传时间抵不过解码时间。

### 9.5 连通域填充：逐像素扫描线 → 行程 + 并查集（2026-11-16 第三轮）

`fillMs` 原本是 12.2 MP 图里最大的一段（86 ms）：逐像素扫描线填充，每行逐个像素找种子，
再整行左右扩张 + 上下邻行逐像素找新种子，12 M 像素要走 3~4 遍；`lab` 是 48 MB 的 int 数组，随机访问也贵。

改成**行程（run）+ 并查集**：

1. 每行先压成若干「暗色连续段」（`Parallel.For`，一遍字节比较）；
2. 相邻两行的段只要区间有交集就 union —— 与原来的上下邻行判定等价（4 邻接）；
3. 每个根汇总面积/质心/外接框，**公式与逐像素实现逐字对应**（连 `(xa+xb)*len/2` 的整数除法位置都一样）；
4. 标签按「行优先首次出现」顺序编号，与原来一一对应，最后并行把 `lab` 整幅写回（下游径向扫描要用）。

| 指标 | 优化前 | 优化后 |
|---|---|---|
| 12.2 MP 单图 `fillMs` | 86 ms | **12.8 ms** |
| 图集1（88 张，中位） | 75.5 ms | **57.5 ms** |
| 图集2（216 张，中位） | 64.4 ms | **43.0 ms** |
| 图集3（38 张，中位） | 126.8 ms | **99.0 ms** |
| 单张 12.2 MP（`/api/detect/fast`） | 126.9 ms | **82.5 ms** |

精度验证：

- `RINGINSPECT_OLD_FILL=1` 可切回逐像素实现做 A/B；
- 三套图集 342 张逐字段比对 **0 差异**（图集3 仍是那 6 张断环图 `NO_RING → NG`，属预期）；
- `/api/detect/fast` vs `/api/detect/path/strict` 全量 342 张 **0 差异**；
- WPF 内置检测（同一份源码）与 WebAPI **完全一致 ✅**；BMP 快速解码 14 个变体与 ImageSharp 逐字节一致。

> 现在这条流水线基本是内存带宽受限：12.2 MP 一张要过
> 读文件 35 MB + 解码（35 MB 读、49 MB 写）+ 亮度（48 MB 读、24 MB 写）+ 标签（约 60 MB）+ 径向扫描（随机读约 60 MB）。
> 再往下压只能靠“合并遍历 / 换数据结构”，收益远小于这一轮。

### 9.6 限制

- 单图 ≤ 4000 万像素（约 6300×6300），请求体 ≤ 128 MB。
- 批量接口一次 ≤ 64 张，串行处理；`includeProfile` / `includeOverlay` 会让响应明显变大
  （实测：同一次检测，不带剖线 2.3 KB，带剖线 107 KB，带 overlay 约 1.2 MB），批量时建议关闭。
- 检测是 CPU 密集型，多实例部署时注意每实例的并发数。
