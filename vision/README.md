# 圆环外缘缺陷检测 · WPF 桌面版

把原来网页版的检测能力做成一个 Windows 桌面程序，同时**把两个对外接口一起带上**：

| 接口 | 说明 |
| --- | --- |
| `POST /api/detect/strict` | 严格模式，参数来自本程序里可编辑的**严格模式预设** |
| `POST /api/detect` | 全参数自定义，请求体里的 `params` 与网页版一一对应 |
| 其余 | `/api/detect/{参数名}`（16 个单参数接口）、`/api/detect/batch`、`/api/params`、`/api/health`、`/api/debug/pixels` 全部保留 |

算法不是重写的：`RingInspector` / `OverlayRenderer` / `ImageCodec` / `ParamRegistry` 都在本工程的
`WebApi\Detection\` 里（和独立 API 程序同源、内容一致），路由在 `WebApi\ApiEndpoints.cs`，
所以桌面版和 API 版的结果逐像素一致。

![主界面](docs/screenshot-main.png)

![严格模式预设](docs/screenshot-preset.png)

---

## 1. 运行

```powershell
# 方式一：直接跑编译好的程序
E:\Opencv\RingInspect.Wpf\bin\Debug\net8.0-windows\RingInspect.Wpf.exe

# 方式二：源码运行（会顺带编译）
cd E:\Opencv\RingInspect.Wpf
dotnet run
```

运行要求：**.NET 8 Desktop Runtime** + **ASP.NET Core 8 Runtime**（内嵌 HTTP 接口需要后者）。
本机已装（`dotnet --list-runtimes` 能看到 `Microsoft.WindowsDesktop.App 8.x` 与 `Microsoft.AspNetCore.App 8.x`）。

> **这个工程自带一套 WebAPI**：接口路由和检测算法都在 `WebApi\` 里，不引用、不依赖
> `RingInspect.Api` 工程。只拷 `RingInspect.Wpf` 这个文件夹（对方电脑上没有那个项目）
> 也能 `dotnet build` / `dotnet run`，判定结果一样。详见 [§8](#8-文件结构)。

发布单目录版本：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o E:\Opencv\out\RingInspect.Wpf
```

---

## 2. 界面（照网页版做的）

结果图不是"把叠加层烤进位图"再缩放，而是和网页 canvas 一样**按当前缩放现画**：

- 线宽 / 虚线 / 字号都除以缩放比，屏幕上恒定 1.5px 细线、13px 标签（放大 5 倍也不会变粗变大）；
- 缩放 ≥ 2 时图片用最近邻，像素块清晰；< 2 时用高质量平滑 —— 和网页的 `imageSmoothingEnabled = v.s < 2` 一致；
- 滚轮以鼠标为锚点，`适应窗口` 留 4% 边距，点缺陷自动放大到 `max(w,h,30)+80` 的取景框；
- 五个图层的默认勾选也和原页面一致：拟合圆 ✓、检测带 ✓、外缘轮廓 ☐、缺陷框 ✓、已忽略缺口 ☐。

![网页 vs 桌面](docs/compare-web-vs-wpf.png)

左：网页 canvas 2D；右：WPF。同一张 case02、同样只勾"拟合圆/检测带/缺陷框"。

- **左栏**：图片列表（每张带 OK/NG/错误 徽标、缺陷数），标题右边的 `清空` 可以一次清掉整个列表和所有检测结果
  （会先弹确认框；参数和预设不动，磁盘上的图片不会被删），
  下面是 16 个参数滑块，
  底部统计条和网页版 `sumline` 一样：**共 N 张 · NG x · OK y · 错误 z**（没有错误时不显示"错误"），
  右边是 **全部 / 仅 NG** 两个筛选（选中项绿色实心，切"仅 NG"只列有缺陷的图，统计数字仍是全部张数），
  下面一行是 `缺陷合计 N 处 · NG 率 xx%` 和「全部重检」。
- **中栏**：检测结果图。工具栏五个勾选框对应网页版的图层：拟合圆 / 检测带 / 外缘轮廓 / 缺陷框 / 已忽略缺口；
  滚轮缩放、按住左键拖动、`适应窗口`、`1:1`、`重新检测本张`。
  **点列表里任意一张图（包括第一张、批量检测完成时自动选中的那张）都会自动适应窗口**，不会沿用上一张的缩放比例。
  只要没手动缩放/拖动过，窗口拉伸、最大化时也会自动保持铺满；手动缩放或拖动后就不再自动，点「适应窗口」恢复。
- **右栏**：结论（NG/OK + 圆心/半径/比例 + 耗时）、外缘偏差曲线（蓝线为 `dev[]`，橙色虚线为缺口阈值，红点为缺陷角度）、缺陷列表（缩略图 + 角度/位置/深度/面积等，和网页版同一套文案）。

快捷键：`↑ ↓` 切换图片，`← →` 切换缺陷（自动放大到该缺陷），`R` 重新检测，`+ -` 缩放，`0` 适应窗口。
鼠标：滚轮缩放、按住左键拖动平移、**直接点图上的缺陷框也能选中它**（和网页版一样的命中测试）。

### 检测启停开关（界面 + 接口共享同一个状态）

顶栏右上角有一个药丸形开关：**绿色「检测运行中」/ 红色「检测已暂停」**。

- 关掉后：批量检测在**当前这张做完**后停下，剩下的继续排队，状态栏提示 `检测已暂停：还剩 N 张待检测`；
  单张「重新检测本张」也会被拦下；内嵌接口的所有检测请求返回 `HTTP 409 DETECTION_PAUSED`。
- 再打开：自动从断点继续把排队的跑完，不用重新选图。
- 这个状态和接口 `GET·POST /api/switch` 是**同一份**：界面上点一下，`GET /api/switch` 立刻变成 `{"enabled":false,"state":"paused"}`；
  反过来用 `POST /api/switch {"enabled":false}` 关掉，界面上的按钮会立刻变红。
- 实现是一个静态类 `WebApi/Detection/DetectionSwitch.cs`（`Enabled` + `Changed` 事件），
  因为内嵌接口和界面在同一个进程里，所以天然共享。

选图片用「选择图片」（可多选）或「选择文件夹」（含子文件夹），也可以把文件/文件夹直接拖进窗口。

---

## 3. 严格模式预设（本次新增的重点）

严格模式的 15 个参数（`strictPair` 恒为开）可以在这里改并保存：

- 顶栏 `编辑预设…` → 改完 `保存`；
- **恢复默认**：参数区标题右边的 `恢复默认`（确认后一步到位：写回出厂默认值 + 立刻重检列表里的图片）；
  预设对话框里也有 `恢复默认`（只把 15 个值填回默认，点 `保存` 才写盘，方便改单个参数）；
- 保存在**程序运行目录**下的 `strict-preset.json`（纯 JSON，16 个字段，可以手改、可以随程序分发）；
- 保存后**立刻生效**：内嵌接口 `POST /api/detect/strict` 下一次请求就用新预设，不用重启；
- 保存预设后，列表里已有的图片会自动按新预设重检一遍（NG/OK 徽标跟着刷新）；
- 界面右上角显示当前预设摘要（缺口深度 / 配对距离 / 碎屑阈值 / 最小面积）；
- 顶栏可以在 `严格模式`（用预设，滑块变灰）与 `自定义参数`（滑块可调，不受预设影响）之间切换。

预设文件示例（没写的字段用默认值）：

```json
{
  "notchDepth": 26.5,
  "debrisTh": 200,
  "minArea": 14,
  "pairDist": 40,
  "strictPair": true
}
```

实测：把 `notchDepth` 从 14 改成 30 后，`case02_notch_spike.png`（缺口深 28.1 px）由 NG 变 OK，`/api/detect` 传 `{"params":{"notchDepth":14}}` 仍然是 NG。

---

## 4. 内嵌 HTTP 接口

程序启动时自动在 `http://127.0.0.1:5080` 起 Kestrel（和 API 项目默认端口一致），底栏能看到状态灯与地址。
**接口就跑在 WPF 自己的进程里**（`Microsoft.AspNetCore.App` 框架引用 + 进程内 `WebApplication`），
任务管理器里只有一个 `RingInspect.Wpf.exe`，监听 5080 的 pid 就是这个 pid，没有额外的 dotnet 进程：

- 端口被占用（比如原来的 `RingInspect.Api` 正在跑）时状态灯变红、按钮变「重试」，改地址后点「启动」即可；
- 点「停止」可以关掉接口，只当纯桌面程序用；
- 检测启停开关也走接口：`GET /api/switch` 查、`POST /api/switch {"enabled":false}` 改，和界面按钮是同一个状态；
- 请求/响应格式、错误码、字段名与独立 API 程序（仓库 `ring-inspect-api`）**完全一致**，接口文档见
  [API接口文档.md](https://gitee.com/huanqing6/ring-inspect-api/blob/main/API%E6%8E%A5%E5%8F%A3%E6%96%87%E6%A1%A3.md)
  （本机在 `E:\Opencv\_gitee_clone\API接口文档.md`）。

```powershell
curl.exe -X POST http://127.0.0.1:5080/api/detect/strict -H "Content-Type: application/json" `
  -d "{\"image\":\"<base64>\"}"
```

---

## 5. 联机（上位机对接）

识别程序做**服务端**：本机监听 TCP（默认 **800**，可配），上位机连上后保持一条**长连接**；
发 `start` 开始，之后每发一条 `check`（可带图片路径，不带就用界面里配的「默认文件夹 + 默认文件名」）
识别检测一张，在同一连接上回 `skip`（无错，继续跑）/ `stop <个数> <缺陷坐标…>`（NG，停机）/
`error <原因>`（检测失败）；`radius` 只测外圆半径；发 `end` 结束。

- 主界面右上角「联机…」打开设置窗口：TCP 端口、API 端口、默认文件夹/文件名、联机日志；
- **设置会持久化**：点「保存」立刻写 `%APPDATA%\RingInspect\settings.json`（写不进去自动退到
  `%LOCALAPPDATA%`、再不行退到程序目录，提示里显示实际路径），下次启动自动按保存的
  TCP 端口/API 端口/默认文件生效（点「停止监听」也会记住下次不自动监听）；写盘失败会在窗口里
  直接报原因；改了没保存就关窗会先提醒。位置也可以用环境变量 `RINGINSPECT_SETTINGS` 指定；
- 右上角状态灯：灰=未监听，黄=监听中待 start，绿=接收中；底栏状态栏同步显示每次收发与判定；
- 没收到 `start` 之前，`check` 一律回 `error 未开始`；`/api/detect/fast` 返回 `409 NOT_ARMED`；
- 上位机原地覆盖同一张图时，识别会重试 3 次并核对文件长度/修改时间，避免读到半张图。

完整协议（命令表、stop 坐标格式、错误码、自测方法）见 [联机协议.md](联机协议.md)。

![联机设置：端口、默认检测文件都在这里配，保存后下次启动自动生效](docs/screenshot-link.png)

---

## 6. 检测方式与速度

左侧「检测方式」下拉三选一：

| 方式 | 做什么 | 图集1 2448×2048（15 MB） | 图集3 4024×3036（35 MB） |
|---|---|---|---|
| **本地检测**（默认） | 进程内直接算，带预览与剖面曲线 | **34.8 ms/张** | **75.0 ms/张** |
| 走接口·批量传地址 | 把文件**地址** `POST /api/detect/path/batch`（每次 ≤64 张，带剖面） | 47.0 ms/张 | 94.2 ms/张 |
| 走接口·极速传地址 | 把文件**地址** `POST /api/detect/fast`（严格预设，只回判定+缺陷） | 46.7 ms/张 | 95.2 ms/张 |

三种方式**判定逐张一致**（图集1 + 图集3 共 126 张，0 差异）；接口方式比本地多的是 HTTP 与 JSON 往返（12~19 ms/张）。

走接口时界面只发路径，不发像素；返回结果直接填进列表。极速方式不返回剖面，曲线区会显示「极速模式：这次只回了判定与缺陷，没算剖面」，
叠加层仍然画圆和缺陷框（不算环带/外缘）。接口的「严格预设」就是界面里保存的那份，所以选走接口会自动切到严格模式。

### 本轮速度优化（本地检测，判定不变）

| 项目 | 之前 | 现在 |
|---|---|---|
| 读图 + 解码 | `File.ReadAllBytes` → `Convert.ToBase64String` → `TryDecode`（多两次 15~47 MB 搬运） | `ImageCodec.TryDecodeFile`（未清零直读 + 直接解码） |
| 预览位图 | 每张都建（12 MP 图 ~46 ms，建完就丢） | 只给**当前选中**那张建 |
| RGBA → BGRA | 逐字节赋值 | 32 位整像素换字节 |
| **图集1 每张** | 197.1 ms | **34.8 ms（5.8x）** |
| **图集3 每张** | 439.8 ms | **75.0 ms（6.2x）** |

自测：`RingInspect.Wpf.exe --bench=<目录> --benchout=<txt>`，会同一批图分别跑老写法和新写法并输出中位数。

---

## 7. 自测开关（自动化用，正常使用不用管）

```powershell
RingInspect.Wpf.exe --files=<目录> --shot=<png> --noapi [--select=1] [--defect=0] [--layers=11010] [--custom] [--param=notchDepth=30] [--filter=ng] [--switch=off] [--clear]
RingInspect.Wpf.exe --shotpreset=<png> --noapi      # 只截预设对话框
RingInspect.Wpf.exe --serve --api=http://127.0.0.1:5091 --files=<目录>   # 起接口不截图，用来测 HTTP
RingInspect.Wpf.exe --presetrt --shot=<png> --noapi # 预设保存/读回自测
RingInspect.Wpf.exe --resetpreset --shot=<png> --noapi  # 恢复默认预设自测
RingInspect.Wpf.exe --bench=<目录> --benchout=<txt>     # 本地检测速度 A/B（老写法 vs 新写法）
RingInspect.Wpf.exe --tcp=800 --folder=... --filename=... --savelink  # 写联机配置后退出
```

`--savelink` 把命令行给的联机配置**落盘**到 settings.json（不启动界面、不监听端口），用于部署脚本；
想看当前生效的配置，直接启动一次，联机窗口的联机日志第一行就会打印
`配置：TCP …｜API …｜默认文件 …`。

`--clear` 会在截图前调用一次「清空」（不弹确认框），用来核对空列表状态；
![清空后的界面](docs/screenshot-cleared.png)

`--shot` 会把窗口内容渲染成 PNG 再退出，方便在无人值守环境下核对界面；
`--layers=11010` 依次对应 拟合圆/检测带/外缘轮廓/缺陷框/已忽略缺口 五个图层。

---

## 8. 文件结构

```
RingInspect.Wpf/
  RingInspect.Wpf.csproj       net8.0-windows + UseWPF + Microsoft.AspNetCore.App
  App.xaml / App.xaml.cs       颜色、按钮、列表项样式；未处理异常写 crash.log
  MainWindow.xaml / .cs        三栏界面、检测队列、缩放、曲线、接口开关
  StrictPresetWindow.xaml/.cs  严格模式预设对话框
  Models.cs                    FileItem / DefectRow / ParamRow
  ParamPanelBuilder.cs         按 ParamRegistry 生成参数面板（滑块 + 数值框）
  UiHelpers.cs                 RGBA→BitmapSource、缺陷缩略图裁剪、配色
  Services/StrictPreset.cs     预设读写（程序运行目录 strict-preset.json）
  Services/ApiHost.cs          内嵌 Kestrel + ApiEndpoints.Map
  WebApi/ApiEndpoints.cs       这套接口的路由实现（本工程自己的）
  WebApi/Detection/*.cs        检测内核：RingInspector / DetectParams / ImageCodec / Models / OverlayRenderer / DetectionSwitch
```

### 接口实现是 WPF 自己的（`WebApi\`）

`RingInspect.Wpf\WebApi\` 是这个工程**自己维护的一整套 WebAPI 实现**：Kestrel 由
`Services\ApiHost.cs` 起在本进程里，路由在 `WebApi\ApiEndpoints.cs`，算法在 `WebApi\Detection\`。

- **不引用、不依赖任何别的工程**：csproj 里没有任何 `..\` 的路径，只靠 SDK 默认的
  `**/*.cs` 编译本工程的文件。把 `RingInspect.Wpf` 这个文件夹单独拷走也能
  `dotnet build` / `dotnet run`。
- **两套各自演进**：独立的最小 API 程序在**另一个仓库** `ring-inspect-api`（本机检出在
  `E:\Opencv\_gitee_clone`），和这里的 `WebApi\` 是平级的两套实现，互不引用、互不自动同步。
  改桌面版的行为就改 `WebApi\`，改独立 API 就去那个仓库。
- 两边当前内容一致（历史原因：桌面版最早就是链这份源码编出来的），所以判定结果完全相同；
  以后谁先改谁生效，不再互相牵动。

```powershell
# 只带 WPF 文件夹时，编译/运行方式和平常一样
cd D:\RingInspect.Wpf
dotnet build -c Release
dotnet run
```

实测（本机脚本 `compare\parity_wpfonly.py`，`compare\` 不进仓库；两份产物分别起在
15091 / 15092 对同一批图比对完整结果 JSON，去掉耗时字段后逐字段比）：图集1/2/3 各取 4 张，
**12 张全一致**，`verdict` / 缺陷数 / 每个缺陷坐标 / 候选数 / `scale` 全部相同。协议用例
（本机脚本 `compare\link_e2e.py`，20 项）也在**这份单独编译的产物**上跑过，
`skip`/`stop`/重连全部通过。
