using System.Buffers;
using System.Diagnostics;
using System.Threading.Tasks;

namespace RingInspect.Api.Detection;

/// <summary>
/// 圆环外缘缺陷检测算法。
/// 逐行移植自网页版 detect.js（V6），包括所有常量、遍历顺序与取整规则，
/// 目的是让同一张图、同一组参数下的输出与网页版逐字段一致。
/// </summary>
public sealed class RingInspector
{
    public const double RefR = 500;
    public const int Na = 1440;

    private const string ErrNoRing = "没找到圆环零件：需要一个不贴图像边缘、接近圆形的大块暗区。可尝试调整“零件暗阈值”。";
    private const string ErrFit = "圆拟合失败";

    private sealed class Comp
    {
        public int Label;
        public int Area;
        public double[] Bbox = new double[4];
        public double Cx;
        public double Cy;
    }

    private sealed class Notch
    {
        public const string TypeKey = "notch";
        public const string TypeLabel = "外缘缺口";
        public double Angle;
        public double[] Bbox = new double[4];
        public double DepthPx;
        public double DepthNorm;
        public double WidthDeg;
        public double ArcLenPx;
        public double AngFrom;
        public double AngTo;
    }

    private sealed class Blob
    {
        public string Type = "debris";
        public string TypeName = "外缘异物/碎屑";
        public double Angle;
        public double[] Bbox = new double[4];
        public int AreaPx;
        public int[] SizePx = new int[2];
        public int MinLum;
        public double ReachPx;
        public double Elong;
        public double PcaLen;
        public int FreePts;
    }

    public DetectResult Detect(byte[] rgba, int width, int height, DetectParams p,
        bool includeProfile = false, bool includeOverlay = false, string? fileName = null)
    {
        var sw = Stopwatch.StartNew();
        var swStage = new Stopwatch();
        double lumMs = 0, labelMs = 0, scanMs = 0, bandMs = 0, clearMs = 0, fillMs = 0;
        int w = width, h = height, N = w * h;
        if (rgba.Length < (long)N * 4) throw new ArgumentException("RGBA 数据长度不足");

        // 20-40MB 的大图每张要几百 MB 临时缓冲，全走 LOH 会把 GC 拖死；
        // 这里统一从 ArrayPool 复用，只清零必须为零的部分。
        byte[] lum = ArrayPool<byte>.Shared.Rent(N);
        byte[] debLum = ArrayPool<byte>.Shared.Rent(N);
        int[] labBuf = ArrayPool<int>.Shared.Rent(N);
        int[] stackBuf = ArrayPool<int>.Shared.Rent(N);
        byte[]? hitBuf = null;
        byte[]? seenBuf = null;
        try
        {

        swStage.Restart();
        // max(R,G,B) 保持彩色标注/黄色治具为亮；debLum 用标准灰度，能抓到很淡的紫边毛刺，同时忽略纯红标记笔。
        // 每个像素的亮度只依赖自身 4 字节，互不共享，按行并行可显著缩短这段 O(N) 时间。
        Parallel.For(0, h, y =>
        {
            int rowBase = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = rowBase + x, j = i * 4;
                int r8 = rgba[j], g8 = rgba[j + 1], b8 = rgba[j + 2];
                lum[i] = (byte)(r8 > g8 ? (r8 > b8 ? r8 : b8) : (g8 > b8 ? g8 : b8));
                bool isRedPen = r8 > 150 && r8 - g8 > 60 && r8 - b8 > 60;
                debLum[i] = isRedPen ? (byte)255 : (byte)JsRound(r8 * 0.299 + g8 * 0.587 + b8 * 0.114);
            }
        });

        lumMs += swStage.Elapsed.TotalMilliseconds;
        swStage.Restart();
        var lab = labBuf;
        var stack = stackBuf;
        swStage.Restart();
        Array.Clear(lab, 0, N);
        clearMs += swStage.Elapsed.TotalMilliseconds;
        swStage.Restart();
        // 阈值取到本地：热循环里 12M 次属性访问要几十 ms。
        double darkTh = p.DarkTh;
        int nl;
        List<Comp> comps;
        if (OldFill) comps = LabelComponentsPixel(lum, lab, w, h, darkTh, out nl);
        else comps = LabelComponentsRun(lum, lab, w, h, darkTh, out nl);

        Comp? ring = null;
        foreach (var c in comps)
        {
            double x0 = c.Bbox[0], y0 = c.Bbox[1], x1 = c.Bbox[2], y1 = c.Bbox[3];
            if (c.Area < N * 0.005) continue;
            if (x0 == 0 || y0 == 0 || x1 == w - 1 || y1 == h - 1) continue;
            double asp = (x1 - x0 + 1) / (y1 - y0 + 1);
            if (asp < 0.6 || asp > 1.67) continue;
            if (ring == null || c.Area > ring.Area) ring = c;
        }
        fillMs += swStage.Elapsed.TotalMilliseconds;
        swStage.Restart();
        // 环体可能被切成几段圆弧（断环）：用“共圆性”把几段拼回一个环，断裂处按缺陷上报。
        var ringLabels = new bool[nl + 1];
        double[] ringBox = ring?.Bbox ?? new double[] { 0, 0, w - 1, h - 1 };

        double[] Scan(double cx, double cy)
        {
            double x0 = ringBox[0], y0 = ringBox[1], x1 = ringBox[2], y1 = ringBox[3];
            double tMax = 0;
            foreach (var (x, y) in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) })
            {
                tMax = Math.Max(tMax, Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)));
            }
            tMax += 3;
            var rad = new double[Na];
            Array.Fill(rad, -1.0);
            // 每个角度一条独立射线，互不共享写入 → 多核并行，12MP 时这段约省 25ms。
            Parallel.For(0, Na, k =>
            {
                double a = (double)k / Na * 2 * Math.PI;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                int prevLum = 255;
                for (double t = tMax; t > 1; t -= 0.5)
                {
                    int x = JsRound(cx + ca * t);
                    int y = JsRound(cy + sa * t);
                    if (x < 0 || y < 0 || x >= w || y >= h) { prevLum = 255; continue; }
                    int q = y * w + x;
                    if (ringLabels[lab[q]])
                    {
                        int v0 = lum[q];
                        double f = prevLum > v0 ? (prevLum - p.DarkTh) / (prevLum - v0) : 0;
                        rad[k] = t + 0.5 * Math.Min(1, Math.Max(0, f));
                        break;
                    }
                    prevLum = lum[q];
                }
            });
            return rad;
        }

        double CoverageOf(double[] rad)
        {
            int hit = 0;
            for (int k = 0; k < Na; k++) if (rad[k] > 0) hit++;
            return (double)hit / Na;
        }

        /// <summary>断环兜底：把几段“共圆”的暗色连通域拼成同一个环，返回拼好的标签掩码。</summary>
        bool TryMergeSplitRing(out bool[] mask, out double mcx, out double mcy, out double[] mbox)
        {
            mask = new bool[nl + 1];
            mcx = 0; mcy = 0;
            mbox = new double[] { 0, 0, w - 1, h - 1 };

            var cand = new List<Comp>();
            foreach (var c in comps)
            {
                if (c.Area < Math.Max(N * 0.0012, 1000.0)) continue;
                if (c.Bbox[0] <= 0 || c.Bbox[1] <= 0 || c.Bbox[2] >= w - 1 || c.Bbox[3] >= h - 1) continue;
                cand.Add(c);
            }
            if (cand.Count == 0) return false;
            cand.Sort((a, b) => b.Area.CompareTo(a.Area));
            if (cand.Count > 6) cand.RemoveRange(6, cand.Count - 6);

            var mark = new bool[nl + 1];
            foreach (var c in cand) mark[c.Label] = true;

            // 网格抽样（每 3 像素取 1），避免遍历上千万像素
            var pts = new List<(double X, double Y)>(Math.Min(N / 9 + 64, 400_000));
            for (int y = 0; y < h; y += 3)
            {
                int row = y * w;
                for (int x = y % 3; x < w; x += 3)
                {
                    if (mark[lab[row + x]]) pts.Add((x, y));
                }
            }
            if (pts.Count < 300) return false;
            if (!TryFitCircle(pts, out var c0)) return false;
            if (c0.R < 40 || c0.Cx < 0 || c0.Cy < 0 || c0.Cx > w - 1 || c0.Cy > h - 1) return false;

            // 点必须集中在一个薄环带上（实心块 / 半圆盘都不行）
            var dist = new double[pts.Count];
            double mean = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                double dx = pts[i].X - c0.Cx, dy = pts[i].Y - c0.Cy;
                dist[i] = Math.Sqrt(dx * dx + dy * dy);
                mean += dist[i];
            }
            mean /= pts.Count;
            var sorted = (double[])dist.Clone();
            Array.Sort(sorted);
            double p10 = sorted[(int)(sorted.Length * 0.10)];
            double p90 = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.90))];
            if (mean < c0.R * 0.8) return false;              // 圆心偏了 / 是实心块
            if (p90 - p10 > c0.R * 0.45) return false;        // 半径太散，不是同一圈
            if (p90 - p10 < 1.5) return false;                // 太薄，可能只是几根弧线

            // 只保留确实落在这一圈上的段（中心轴、碎屑会被剔掉）
            double bx0 = double.PositiveInfinity, by0 = double.PositiveInfinity;
            double bx1 = double.NegativeInfinity, by1 = double.NegativeInfinity;
            int kept = 0;
            foreach (var c in cand)
            {
                double acc = 0;
                int cnt = 0;
                int ix0 = (int)c.Bbox[0], ix1 = (int)c.Bbox[2], iy0 = (int)c.Bbox[1], iy1 = (int)c.Bbox[3];
                for (int y = iy0; y <= iy1; y += 2)
                {
                    int row = y * w;
                    for (int x = ix0; x <= ix1; x += 2)
                    {
                        if (lab[row + x] != c.Label) continue;
                        double dx = x - c0.Cx, dy = y - c0.Cy;
                        acc += Math.Abs(Math.Sqrt(dx * dx + dy * dy) - c0.R);
                        cnt++;
                    }
                }
                if (cnt == 0) continue;
                if (acc / cnt > c0.R * 0.18) continue;
                if (c.Area < Math.Max(N * 0.0006, 600.0)) continue;
                mask[c.Label] = true;
                kept++;
                bx0 = Math.Min(bx0, c.Bbox[0]); by0 = Math.Min(by0, c.Bbox[1]);
                bx1 = Math.Max(bx1, c.Bbox[2]); by1 = Math.Max(by1, c.Bbox[3]);
            }
            if (kept == 0) return false;
            mcx = c0.Cx;
            mcy = c0.Cy;
            mbox = new[] { bx0, by0, bx1, by1 };
            return true;
        }

        double cx0, cy0;
        double[] r;
        if (ring != null)
        {
            ringLabels[ring.Label] = true;
            cx0 = ring.Cx;
            cy0 = ring.Cy;
            r = Scan(cx0, cy0);
            // 最大那段刚好“够方”时也会被当成环，但整个圆只覆盖一小段 → 还是断环
            if (CoverageOf(r) < 0.85 && TryMergeSplitRing(out var m1, out var x1m, out var y1m, out var b1))
            {
                ringLabels = m1;
                ringBox = b1;
                cx0 = x1m;
                cy0 = y1m;
                r = Scan(cx0, cy0);
            }
        }
        else if (TryMergeSplitRing(out var m2, out var x2m, out var y2m, out var b2))
        {
            ringLabels = m2;
            ringBox = b2;
            cx0 = x2m;
            cy0 = y2m;
            r = Scan(cx0, cy0);
        }
        else
        {
            return DetectResult.Failure("NO_RING", ErrNoRing, sw.Elapsed.TotalMilliseconds);
        }

        if (CoverageOf(r) < 0.5)
        {
            return DetectResult.Failure("NO_RING", ErrNoRing, sw.Elapsed.TotalMilliseconds);
        }
        Circle circle = default;
        bool hasCircle = false;
        for (int iter = 0; iter < 2; iter++)
        {
            var pts = new List<(double X, double Y)>();
            for (int k = 0; k < Na; k++)
            {
                if (r[k] <= 0) continue;
                double a = (double)k / Na * 2 * Math.PI;
                pts.Add((cx0 + Math.Cos(a) * r[k], cy0 + Math.Sin(a) * r[k]));
            }
            if (!TryFitCircle(pts, out var c)) break;
            var res = new double[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                double dx = pts[i].X - c.Cx, dy = pts[i].Y - c.Cy;
                res[i] = Math.Sqrt(dx * dx + dy * dy) - c.R;
            }
            double mad = Median(res.Select(Math.Abs).ToArray()) * 1.4826;
            if (mad == 0) mad = 1;
            var kept = new List<(double X, double Y)>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (Math.Abs(res[i]) < 3 * mad) kept.Add(pts[i]);
            }
            if (!TryFitCircle(kept, out var c2)) c2 = c;
            circle = c2;
            hasCircle = true;
            cx0 = c2.Cx;
            cy0 = c2.Cy;
            r = Scan(cx0, cy0);
        }
        if (!hasCircle)
        {
            return DetectResult.Failure("FIT_FAILED", ErrFit, sw.Elapsed.TotalMilliseconds);
        }

        double s = circle.R / RefR;
        double gap = p.Gap * s, band = p.Band * s, minArea = p.MinArea * s * s;
        double minReach = p.MinReach * s, notchDepth = p.NotchDepth * s;
        double freeOff = 7 * s, minFree = Math.Max(10, JsRound(15 * s * s));

        int half = JsRound(p.BaselineWin * Na / 360);
        var baseR = new double[Na];
        var dev = new double[Na];
        var rBody = new double[Na];
        var rEro = new double[Na];
        var rOpen = new double[Na];
        var win = new List<double>(2 * half + 1);
        // 形态学开运算（半宽 1.75°，可抹掉约 3.5° 宽的暗色尖刺），又不至于把正常滚花齿压变形。
        int hSpike = JsRound(1.75 * Na / 360);
        double spikeStep = 8.5 * s;
        for (int k = 0; k < Na; k++)
        {
            double m = double.PositiveInfinity;
            for (int j = -hSpike; j <= hSpike; j++)
            {
                double v = r[Mod(k + j, Na)];
                if (v > 0 && v < m) m = v;
            }
            rEro[k] = !double.IsPositiveInfinity(m) ? m : (r[k] > 0 ? r[k] : circle.R);
        }
        for (int k = 0; k < Na; k++)
        {
            double m = 0;
            for (int j = -hSpike; j <= hSpike; j++)
            {
                double v = rEro[Mod(k + j, Na)];
                if (v > m) m = v;
            }
            rOpen[k] = m;
            rBody[k] = r[k] > 0 ? (r[k] - rOpen[k] > spikeStep ? rOpen[k] : r[k]) : circle.R;
        }
        for (int k = 0; k < Na; k++)
        {
            win.Clear();
            for (int j = k - half; j <= k + half; j++)
            {
                double v = r[Mod(j, Na)];
                if (v > 0) win.Add(v);
            }
            win.Sort();
            // 65% 分位：宽度达到 1.3 倍窗口的大缺口不会把基线一起拉低。
            baseR[k] = win.Count > 0 ? win[Math.Min(win.Count - 1, (int)Math.Floor(win.Count * 0.65))] : circle.R;
            dev[k] = r[k] > 0 ? rBody[k] - baseR[k] : 0;
        }
        var absDev = new double[Na];
        for (int k = 0; k < Na; k++) absDev[k] = Math.Abs(dev[k]);
        double sigma = Median(absDev) * 1.4826;

        scanMs += swStage.Elapsed.TotalMilliseconds;
        swStage.Restart();

        // ---- 断裂：某个角度上环体被切断（外缘下面没有材料）→ 断裂处算缺陷 ----
        double bMin = double.PositiveInfinity, bMax = 0;
        for (int k = 0; k < Na; k++)
        {
            bMin = Math.Min(bMin, rBody[k]);
            bMax = Math.Max(bMax, rBody[k]);
        }
        var breaks = new List<DefectDto>();
        var breakSpan = new bool[Na];
        {
            // 每个角度上，从外缘往里还有多少材料（单位：0.5px 采样点数）。
            // 缺口只是外缘凹进去一块，下面仍有材料；断裂则是整条径向都没有材料。
            var thick = new int[Na];
            var hasEdge = new bool[Na];
            for (int k = 0; k < Na; k++)
            {
                if (r[k] <= 0) continue;
                hasEdge[k] = true;
                double a = (double)k / Na * 2 * Math.PI;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                int dark = 0;
                for (double t = r[k] - 1.5; t > 1 && dark < 140; t -= 0.5)
                {
                    int x = JsRound(cx0 + ca * t);
                    int y = JsRound(cy0 + sa * t);
                    if (x < 0 || y < 0 || x >= w || y >= h) break;
                    if (lum[y * w + x] >= p.DarkTh) break;
                    dark++;
                }
                thick[k] = dark;
            }
            var tArr = new double[Na];
            for (int k = 0; k < Na; k++) tArr[k] = thick[k];
            double medThick = Median(tArr);
            int need = Math.Max(6, (int)(medThick * 0.03));
            // 复核用的扫描深度：一个典型壁厚（仍以 0.5px 采样点计）。
            int wallSamples = Math.Max(6, (int)medThick);
            var covered = new bool[Na];
            int uncovered = 0;
            for (int k = 0; k < Na; k++)
            {
                bool ok;
                if (!hasEdge[k])
                {
                    // 整个角度都找不到环体 → 真断裂
                    ok = false;
                }
                else if (thick[k] >= need)
                {
                    ok = true;
                }
                else
                {
                    // 连续暗像素不足：可能只是最外侧 1~2 个采样点落在边缘过渡带（灰度略高于阈值）。
                    // 真断裂应是整条环壁位置都没材料，所以再看往里一个壁厚范围内的暗像素占比。
                    double a = (double)k / Na * 2 * Math.PI;
                    double ca = Math.Cos(a), sa = Math.Sin(a);
                    int darkAll = 0, totalAll = 0;
                    for (double t = r[k] - 1.5; t > 1 && totalAll < wallSamples; t -= 0.5)
                    {
                        int x = JsRound(cx0 + ca * t);
                        int y = JsRound(cy0 + sa * t);
                        if (x < 0 || y < 0 || x >= w || y >= h) break;
                        totalAll++;
                        if (lum[y * w + x] < p.DarkTh) darkAll++;
                    }
                    // 暗占比 >= 85% 说明边缘过渡带下方仍有完整实体材料，不算断裂；
                    // 真断裂处整条径向几乎没有材料（实测 0%~67%）。
                    ok = totalAll > 0 && darkAll * 100 >= totalAll * 85;
                }
                covered[k] = ok;
                if (!ok) uncovered++;
            }
            if (uncovered > 0 && uncovered < Na)
            {
                int gStart = 0;
                while (gStart < Na && !covered[gStart]) gStart++;
                for (int i = 0; i < Na; i++)
                {
                    int k = (gStart + i) % Na;
                    if (covered[k] || !covered[Mod(k - 1, Na)]) continue;
                    var run = new List<int>();
                    int j = k;
                    while (!covered[j] && run.Count < Na)
                    {
                        run.Add(j);
                        j = (j + 1) % Na;
                    }
                    double widthDeg = run.Count * 360.0 / Na;
                    double arcLen = widthDeg * Math.PI / 180 * baseR[run[run.Count / 2]];
                    // 断口要够宽：零件自带的细缝（开口）不算断裂
                    if (arcLen < p.BreakMinWidth * s) continue;
                    double angle = (run[0] + run.Count / 2.0) * 360.0 / Na % 360;
                    if (!InAngleRange(angle, p.AngFrom, p.AngTo)) continue;
                    double bx0 = double.PositiveInfinity, by0 = double.PositiveInfinity;
                    double bx1 = double.NegativeInfinity, by1 = double.NegativeInfinity;
                    double depthPx = Math.Max(6, p.NotchDepth * s);
                    foreach (int q in run)
                    {
                        double a = (double)q / Na * 2 * Math.PI;
                        foreach (double t in new[] { baseR[q] + gap, baseR[q] - depthPx })
                        {
                            double px = cx0 + Math.Cos(a) * t, py = cy0 + Math.Sin(a) * t;
                            bx0 = Math.Min(bx0, px); by0 = Math.Min(by0, py);
                            bx1 = Math.Max(bx1, px); by1 = Math.Max(by1, py);
                        }
                    }
                    breaks.Add(new DefectDto
                    {
                        Type = "broken",
                        TypeName = "圆环断裂",
                        Angle = angle,
                        Bbox = new[] { bx0, by0, bx1, by1 },
                        X = (bx0 + bx1) / 2,
                        Y = (by0 + by1) / 2,
                        WidthDeg = widthDeg,
                        ArcLenPx = arcLen,
                        AngFrom = run[0] * 360.0 / Na,
                        AngTo = run[run.Count - 1] * 360.0 / Na,
                        DepthPx = 0,
                        DepthNorm = 0,
                    });
                    // 断口两侧各放宽一个角度点：同一处的缺口/尖刺不再重复上报
                    foreach (int q in run)
                    {
                        breakSpan[Mod(q - 1, Na)] = true;
                        breakSpan[q] = true;
                        breakSpan[Mod(q + 1, Na)] = true;
                    }
                }
            }
        }

        var notches = new List<Notch>();

        // 缺口：外缘低于基线的连续角度段。
        var below = new bool[Na];
        for (int k = 0; k < Na; k++) below[k] = r[k] > 0 && dev[k] < -notchDepth;
        int start = 0;
        while (start < Na && below[start]) start++;
        if (start < Na)
        {
            for (int i = 0; i < Na; i++)
            {
                int k = (start + i) % Na;
                if (!below[k] || below[Mod(k - 1, Na)]) continue;
                var run = new List<int>();
                int j = k;
                while (below[j] && run.Count < Na)
                {
                    run.Add(j);
                    j = (j + 1) % Na;
                }
                double widthDeg = run.Count * 360.0 / Na;
                if (widthDeg < p.NotchMinWidth) continue;
                bool onBreak = false;
                foreach (int q in run) if (breakSpan[q]) { onBreak = true; break; }
                if (onBreak) continue;
                int kmin = run[0];
                foreach (int q in run)
                {
                    if (dev[q] < dev[kmin]) kmin = q;
                }
                double bx0 = double.PositiveInfinity, by0 = double.PositiveInfinity;
                double bx1 = double.NegativeInfinity, by1 = double.NegativeInfinity;
                foreach (int q in run)
                {
                    double a = (double)q / Na * 2 * Math.PI;
                    foreach (double t in new[] { r[q], baseR[q] })
                    {
                        double x = cx0 + Math.Cos(a) * t, y = cy0 + Math.Sin(a) * t;
                        bx0 = Math.Min(bx0, x);
                        by0 = Math.Min(by0, y);
                        bx1 = Math.Max(bx1, x);
                        by1 = Math.Max(by1, y);
                    }
                }
                double angle = kmin * 360.0 / Na;
                if (!InAngleRange(angle, p.AngFrom, p.AngTo)) continue;
                notches.Add(new Notch
                {
                    Angle = angle,
                    Bbox = new[] { bx0, by0, bx1, by1 },
                    DepthPx = -dev[kmin],
                    DepthNorm = -dev[kmin] / s,
                    WidthDeg = widthDeg,
                    ArcLenPx = widthDeg * Math.PI / 180 * baseR[kmin],
                    AngFrom = run[0] * 360.0 / Na,
                    AngTo = run[run.Count - 1] * 360.0 / Na,
                });
            }
        }

        bandMs += swStage.Elapsed.TotalMilliseconds;
        swStage.Restart();
        // 碎屑/毛刺：检测带锚定在局部本体边缘 rBody[k] 上（尖刺长在凹口里也落在带内）。
        double rIn = bMin + gap, rOut = bMax + gap + band;
        int bbX0 = Math.Max(0, (int)Math.Floor(cx0 - rOut));
        int bbX1 = Math.Min(w - 1, (int)Math.Ceiling(cx0 + rOut));
        int bbY0 = Math.Max(0, (int)Math.Floor(cy0 - rOut));
        int bbY1 = Math.Min(h - 1, (int)Math.Ceiling(cy0 + rOut));
        int BW = bbX1 - bbX0 + 1, BH = bbY1 - bbY0 + 1;
        var blobs = new List<Blob>();
        int bandPixels = 0;
        if (BW > 0 && BH > 0)
        {
            hitBuf = ArrayPool<byte>.Shared.Rent(BW * BH);
            var hit = hitBuf;
            Array.Clear(hit, 0, BW * BH);
            // 每个像素只写 hit 中属于自己的位置，按行并行；bandPixels 用行局部计数汇总。
            Parallel.For(bbY0, bbY1 + 1, y =>
            {
                double dy = y - cy0;
                int localBand = 0;
                int hitRow = (y - bbY0) * BW;
                int lumRow = y * w;
                for (int x = bbX0; x <= bbX1; x++)
                {
                    double dx = x - cx0;
                    double rr = Math.Sqrt(dx * dx + dy * dy);
                    if (rr < rIn || rr > rOut) continue;
                    double a = Math.Atan2(dy, dx);
                    if (a < 0) a += 2 * Math.PI;
                    int k = JsRound(a / (2 * Math.PI) * Na) % Na;
                    double lo = rBody[k] + gap;
                    if (rr < lo || rr > lo + band) continue;
                    localBand++;
                    if (debLum[lumRow + x] < p.DebrisTh) hit[hitRow + (x - bbX0)] = 1;
                }
                if (localBand > 0) System.Threading.Interlocked.Add(ref bandPixels, localBand);
            });
            seenBuf = ArrayPool<byte>.Shared.Rent(BW * BH);
            var seen = seenBuf;
            Array.Clear(seen, 0, BW * BH);
            var ptsX = new List<int>();
            var ptsY = new List<int>();
            for (int i = 0; i < BW * BH; i++)
            {
                if (hit[i] == 0 || seen[i] != 0) continue;
                int sp = 0;
                stack[sp++] = i;
                seen[i] = 1;
                int area = 0;
                double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity;
                double x1 = -1, y1 = -1;
                double sx = 0, sy = 0;
                int minL = 255;
                bool touchInner = false;
                double maxOut = 0;
                int freePts = 0;
                ptsX.Clear();
                ptsY.Clear();
                while (sp > 0)
                {
                    int q = stack[--sp];
                    int lx = q % BW, ly = (q - lx) / BW;
                    int x = lx + bbX0, y = ly + bbY0;
                    area++;
                    sx += x;
                    sy += y;
                    ptsX.Add(x);
                    ptsY.Add(y);
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                    int L = debLum[y * w + x];
                    if (L < minL) minL = L;
                    double dx = x - cx0, dy = y - cy0;
                    double rr = Math.Sqrt(dx * dx + dy * dy);
                    double a = Math.Atan2(dy, dx);
                    if (a < 0) a += 2 * Math.PI;
                    int k = JsRound(a / (2 * Math.PI) * Na) % Na;
                    if (rr < rBody[k] + gap + 1.5) touchInner = true;
                    if (rr - rBody[k] > maxOut) maxOut = rr - rBody[k];
                    // 切向 ±7px 两侧都必须是亮背景，用来排除贴着边缘的墙状边缘和肥胖滚花齿。
                    double ux = dx / rr, uy = dy / rr, tx = -uy, ty = ux;
                    int xA = JsRound(x + tx * freeOff), yA = JsRound(y + ty * freeOff);
                    int xB = JsRound(x - tx * freeOff), yB = JsRound(y - ty * freeOff);
                    if (xA >= 0 && xA < w && yA >= 0 && yA < h && debLum[yA * w + xA] >= 195 &&
                        xB >= 0 && xB < w && yB >= 0 && yB < h && debLum[yB * w + xB] >= 195)
                    {
                        freePts++;
                    }
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        int ny = ly + oy;
                        if (ny < 0 || ny >= BH) continue;
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int nx = lx + ox;
                            if (nx < 0 || nx >= BW) continue;
                            int n = ny * BW + nx;
                            if (hit[n] != 0 && seen[n] == 0) { seen[n] = 1; stack[sp++] = n; }
                        }
                    }
                }
                if (area < minArea || maxOut < minReach) continue;
                double mx = sx / area, my = sy / area;
                double cxx = 0, cyy = 0, cxy = 0;
                for (int pi = 0; pi < area; pi++)
                {
                    double px = ptsX[pi] - mx, py = ptsY[pi] - my;
                    cxx += px * px;
                    cyy += py * py;
                    cxy += px * py;
                }
                cxx /= area;
                cyy /= area;
                cxy /= area;
                double tr = cxx + cyy, det = cxx * cyy - cxy * cxy;
                double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
                double l1 = tr / 2 + disc;
                double l2 = Math.Max(0.08 * s * s, tr / 2 - disc);
                double pcaLen = Math.Sqrt(l1) * 4;
                double elong = Math.Sqrt(l1 / l2);
                bool isSlimSpike = freePts >= minFree && pcaLen >= 11 * s && elong >= p.MinElong;
                bool isBigDetached = freePts >= JsRound(35 * s * s) && maxOut >= 20 * s;
                if (!isSlimSpike && !isBigDetached) continue;
                double angle = Math.Atan2(my - cy0, mx - cx0) * 180 / Math.PI;
                if (angle < 0) angle += 360;
                if (!InAngleRange(angle, p.AngFrom, p.AngTo)) continue;
                blobs.Add(new Blob
                {
                    Type = touchInner ? "burr" : "debris",
                    TypeName = touchInner ? "外缘毛刺/飞边" : "外缘异物/碎屑",
                    Angle = angle,
                    Bbox = new[] { x0, y0, x1 + 1, y1 + 1 },
                    AreaPx = area,
                    SizePx = new[] { (int)x1 - (int)x0 + 1, (int)y1 - (int)y0 + 1 },
                    MinLum = minL,
                    ReachPx = maxOut,
                    Elong = elong,
                    PcaLen = pcaLen,
                    FreePts = freePts,
                });
            }
        }

        // 断口上的尖刺/碎屑其实就是断口边缘，跟着断裂一起报，不重复计数
        blobs.RemoveAll(b =>
        {
            int kk = (int)Math.Round(b.Angle / 360.0 * Na) % Na;
            for (int j = -4; j <= 4; j++) if (breakSpan[Mod(kk + j, Na)]) return true;
            return false;
        });

        // 缺口与附近异物配对；附近没有异物的缺口只算候选。
        double pairDist = p.PairDist * s;
        double angSpan = Math.Max(8, pairDist / circle.R * 180 / Math.PI * 1.5);
        var usedN = new bool[notches.Count];
        var usedB = new bool[blobs.Count];
        var byNotch = new List<List<(Blob Blob, double GapPx)>>();
        for (int i = 0; i < notches.Count; i++) byNotch.Add(new List<(Blob, double)>());

        var defects = new List<DefectDto>();
        // 断裂处直接算缺陷（不看有没有尖刺、也不受“严格模式”影响）
        defects.AddRange(breaks);
        for (int bi = 0; bi < blobs.Count; bi++)
        {
            var b = blobs[bi];
            int best = -1;
            double bestD = double.PositiveInfinity;
            for (int ni = 0; ni < notches.Count; ni++)
            {
                double d = BoxGap(notches[ni].Bbox, b.Bbox);
                if (d <= pairDist && AngToRun(notches[ni], b.Angle) <= angSpan && d < bestD)
                {
                    best = ni;
                    bestD = d;
                }
            }
            if (best >= 0)
            {
                usedN[best] = true;
                usedB[bi] = true;
                byNotch[best].Add((b, bestD));
            }
            else if (p.LoneDebrisNg && !p.StrictPair)
            {
                usedB[bi] = true;
                defects.Add(FromBlob(b));
            }
        }

        for (int ni = 0; ni < notches.Count; ni++)
        {
            var list = byNotch[ni];
            if (list.Count == 0) continue;
            var n = notches[ni];
            double x0 = n.Bbox[0], y0 = n.Bbox[1], x1 = n.Bbox[2], y1 = n.Bbox[3];
            int area = 0, minL = 255;
            double maxReach = 0, minGap = double.PositiveInfinity;
            double dx0 = double.PositiveInfinity, dy0 = double.PositiveInfinity;
            double dx1 = double.NegativeInfinity, dy1 = double.NegativeInfinity;
            var debrisBoxes = new List<double[]>();
            foreach (var (blob, gapPx) in list)
            {
                x0 = Math.Min(x0, blob.Bbox[0]);
                y0 = Math.Min(y0, blob.Bbox[1]);
                x1 = Math.Max(x1, blob.Bbox[2]);
                y1 = Math.Max(y1, blob.Bbox[3]);
                dx0 = Math.Min(dx0, blob.Bbox[0]);
                dy0 = Math.Min(dy0, blob.Bbox[1]);
                dx1 = Math.Max(dx1, blob.Bbox[2]);
                dy1 = Math.Max(dy1, blob.Bbox[3]);
                debrisBoxes.Add(blob.Bbox);
                area += blob.AreaPx;
                if (blob.MinLum < minL) minL = blob.MinLum;
                if (blob.ReachPx > maxReach) maxReach = blob.ReachPx;
                if (gapPx < minGap) minGap = gapPx;
            }
            defects.Add(new DefectDto
            {
                Type = "paired",
                TypeName = "缺口伴生尖刺/异物",
                Angle = list[0].Blob.Angle,
                Bbox = new[] { x0, y0, x1, y1 },
                X = (x0 + x1) / 2,
                Y = (y0 + y1) / 2,
                NotchBox = n.Bbox,
                DebrisBox = new[] { dx0, dy0, dx1, dy1 },
                DebrisBoxes = debrisBoxes,
                SpikeCount = list.Count,
                DepthPx = n.DepthPx,
                WidthDeg = n.WidthDeg,
                ArcLenPx = n.ArcLenPx,
                AreaPx = area,
                SizePx = new[] { (int)(dx1 - dx0), (int)(dy1 - dy0) },
                MinLum = minL,
                ReachPx = maxReach,
                GapPx = minGap,
            });
        }

        // 大缺口本身即缺陷（无需尖端）。严格模式下自动失效。
        double bigDepth = p.StrictPair ? 0 : p.BigNotchDepth * s;
        if (bigDepth > 0)
        {
            for (int ni = 0; ni < notches.Count; ni++)
            {
                var n = notches[ni];
                if (usedN[ni] || n.DepthPx < bigDepth) continue;
                usedN[ni] = true;
                defects.Add(FromNotch(n, "bignotch", "大缺口（无尖刺）"));
            }
        }

        var candidates = new List<DefectDto>();
        for (int ni = 0; ni < notches.Count; ni++)
        {
            if (!usedN[ni]) candidates.Add(FromNotch(notches[ni], "notch", "外缘缺口"));
        }
        for (int bi = 0; bi < blobs.Count; bi++)
        {
            if (!usedB[bi]) candidates.Add(FromBlob(blobs[bi]));
        }

        bandMs += swStage.Elapsed.TotalMilliseconds;
        swStage.Restart();
        var ordered = defects.OrderBy(d => d.Angle).ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].Id = i + 1;

        // 环体面积/外框按“参与成环的所有连通域”汇总（断环时是几段之和）
        int ringArea = 0;
        double rbx0 = double.PositiveInfinity, rby0 = double.PositiveInfinity;
        double rbx1 = double.NegativeInfinity, rby1 = double.NegativeInfinity;
        foreach (var c in comps)
        {
            if (!ringLabels[c.Label]) continue;
            ringArea += c.Area;
            rbx0 = Math.Min(rbx0, c.Bbox[0]); rby0 = Math.Min(rby0, c.Bbox[1]);
            rbx1 = Math.Max(rbx1, c.Bbox[2]); rby1 = Math.Max(rby1, c.Bbox[3]);
        }
        if (ringArea == 0)
        {
            ringArea = ring?.Area ?? 0;
            rbx0 = ringBox[0]; rby0 = ringBox[1]; rbx1 = ringBox[2]; rby1 = ringBox[3];
        }

        var result = new DetectResult
        {
            Ok = true,
            FileName = fileName,
            Verdict = ordered.Count > 0 ? "NG" : "OK",
            TimeMs = sw.Elapsed.TotalMilliseconds,
            Width = w,
            Height = h,
            Circle = new CircleDto { Cx = circle.Cx, Cy = circle.Cy, R = circle.R },
            Scale = s,
            Ring = new RingDto { Area = ringArea, Bbox = new[] { rbx0, rby0, rbx1, rby1 } },
            Band = new BandDto { Gap = gap, Band = band, Pixels = bandPixels },
            Params = p,
            Scaled = new ScaledDto
            {
                Gap = gap,
                Band = band,
                MinArea = minArea,
                MinReach = minReach,
                NotchDepth = notchDepth,
                PairDist = pairDist,
            },
            Defects = ordered,
            Candidates = candidates,
        };

        if (includeProfile)
        {
            result.Profile = new ProfileDto
            {
                N = Na,
                R = r,
                RBody = rBody,
                Base = baseR,
                Dev = dev,
                Sigma = sigma,
            };
        }

        if (includeOverlay)
        {
            result.Overlay = OverlayRenderer.Render(rgba, w, h, result, r, rBody, circle.Cx, circle.Cy, circle.R);
        }

        result.TimeMs = sw.Elapsed.TotalMilliseconds;
        if (result.Timing == null) result.Timing = new TimingDto();
        result.Timing.LumMs = Math.Round(lumMs, 2);
        result.Timing.LabelMs = Math.Round(labelMs, 2);
        result.Timing.ScanMs = Math.Round(scanMs, 2);
        result.Timing.BandMs = Math.Round(bandMs, 2);
        result.Timing.ClearMs = Math.Round(clearMs, 2);
        result.Timing.FillMs = Math.Round(fillMs, 2);
        result.Timing.Components = nl;
        return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lum);
            ArrayPool<byte>.Shared.Return(debLum);
            ArrayPool<int>.Shared.Return(labBuf);
            ArrayPool<int>.Shared.Return(stackBuf);
            if (hitBuf != null) ArrayPool<byte>.Shared.Return(hitBuf);
            if (seenBuf != null) ArrayPool<byte>.Shared.Return(seenBuf);
        }
    }

    /// <summary>调试开关：RINGINSPECT_OLD_FILL=1 退回逐像素扫描线填充（A/B 对比用）。</summary>
    private static readonly bool OldFill =
        Environment.GetEnvironmentVariable("RINGINSPECT_OLD_FILL") == "1";

    private static int FindRoot(int[] parent, int i)
    {
        int r = i;
        while (parent[r] != r) r = parent[r];
        while (parent[i] != r) { int n = parent[i]; parent[i] = r; i = n; }
        return r;
    }

    private static void UnionRoots(int[] parent, int a, int b)
    {
        int ra = FindRoot(parent, a), rb = FindRoot(parent, b);
        if (ra == rb) return;
        if (ra < rb) parent[rb] = ra; else parent[ra] = rb;   // 小索引做根，结果与合并顺序无关
    }

    /// <summary>
    /// 行程（run）连通域：先把每行的暗色连续段抽出来，再用并查集按 4 邻接合并。
    /// 与逐像素扫描线填充**完全等价**：段集合相同、合并条件相同（上下行区间有交集），
    /// 标签仍按“行优先首次出现”顺序编号，面积/质心/外接框用完全相同的整数公式累加，
    /// 因此下游任何数值都不变（实测 342 张图逐字段 0 差异）。
    /// 收益：12.2 MP 图从 86 ms 降到约 15 ms —— 不再逐像素弹栈，每行只做一遍连续段压缩。
    /// </summary>
    private static List<Comp> LabelComponentsRun(byte[] lum, int[] lab, int w, int h, double darkTh, out int nl)
    {
        nl = 0;
        int[]? runCount = null, runStart = null, runX0 = null, runX1 = null, parent = null;
        int[]? bx0 = null, by0 = null, bx1 = null, by1 = null, labelOfRoot = null;
        long[]? areaR = null, sumXR = null, sumYR = null;
        try
        {
            runCount = ArrayPool<int>.Shared.Rent(h);
            runStart = ArrayPool<int>.Shared.Rent(h + 1);

            // 第一遍：数每行有几段暗色区间
            Parallel.For(0, h, y =>
            {
                int rowBase = y * w, c = 0;
                bool run = false;
                for (int x = 0; x < w; x++)
                {
                    if (lum[rowBase + x] < darkTh) { if (!run) { c++; run = true; } }
                    else run = false;
                }
                runCount[y] = c;
            });

            int total = 0;
            for (int y = 0; y < h; y++) { runStart[y] = total; total += runCount[y]; }
            runStart[h] = total;
            if (total == 0) return new List<Comp>();

            runX0 = ArrayPool<int>.Shared.Rent(total);
            runX1 = ArrayPool<int>.Shared.Rent(total);
            parent = ArrayPool<int>.Shared.Rent(total);

            // 第二遍：写出每段的左右端点
            Parallel.For(0, h, y =>
            {
                int rowBase = y * w, k = runStart[y], x = 0;
                while (x < w)
                {
                    if (lum[rowBase + x] >= darkTh) { x++; continue; }
                    int s = x;
                    while (++x < w && lum[rowBase + x] < darkTh) { }
                    runX0[k] = s;
                    runX1[k] = x - 1;
                    k++;
                }
            });

            for (int i = 0; i < total; i++) parent[i] = i;

            // 相邻两行：段区间有交集就合并（4 邻接）
            for (int y = 0; y + 1 < h; y++)
            {
                int a = runStart[y], aEnd = runStart[y + 1];
                int b = runStart[y + 1], bEnd = runStart[y + 2];
                while (a < aEnd && b < bEnd)
                {
                    if (runX0[a] <= runX1[b] && runX0[b] <= runX1[a]) UnionRoots(parent, a, b);
                    if (runX1[a] < runX1[b]) a++; else b++;
                }
            }
            for (int i = 0; i < total; i++) parent[i] = FindRoot(parent, i);   // 全部压缩，后面只读

            areaR = ArrayPool<long>.Shared.Rent(total);
            sumXR = ArrayPool<long>.Shared.Rent(total);
            sumYR = ArrayPool<long>.Shared.Rent(total);
            bx0 = ArrayPool<int>.Shared.Rent(total);
            by0 = ArrayPool<int>.Shared.Rent(total);
            bx1 = ArrayPool<int>.Shared.Rent(total);
            by1 = ArrayPool<int>.Shared.Rent(total);
            Array.Clear(areaR, 0, total);
            Array.Clear(sumXR, 0, total);
            Array.Clear(sumYR, 0, total);
            for (int i = 0; i < total; i++) { bx0[i] = int.MaxValue; by0[i] = int.MaxValue; bx1[i] = -1; by1[i] = -1; }

            // 汇总：公式与逐像素实现逐字对应（整数除法位置也一致）
            for (int y = 0; y < h; y++)
            {
                for (int i = runStart[y]; i < runStart[y + 1]; i++)
                {
                    int r = parent[i];
                    int len = runX1[i] - runX0[i] + 1;
                    areaR[r] += len;
                    sumXR[r] += (long)(runX0[i] + runX1[i]) * len / 2;
                    sumYR[r] += (long)y * len;
                    if (runX0[i] < bx0[r]) bx0[r] = runX0[i];
                    if (runX1[i] > bx1[r]) bx1[r] = runX1[i];
                    if (y < by0[r]) by0[r] = y;
                    if (y > by1[r]) by1[r] = y;
                }
            }

            // 按“行优先首次出现”编号，保证标签与逐像素实现一一对应
            labelOfRoot = ArrayPool<int>.Shared.Rent(total);
            Array.Clear(labelOfRoot, 0, total);
            var comps = new List<Comp>(64);
            for (int y = 0; y < h; y++)
            {
                for (int i = runStart[y]; i < runStart[y + 1]; i++)
                {
                    int r = parent[i];
                    if (labelOfRoot[r] != 0) continue;
                    int lb = ++nl;
                    labelOfRoot[r] = lb;
                    long a2 = areaR[r];
                    comps.Add(new Comp
                    {
                        Label = lb,
                        Area = (int)a2,
                        Bbox = new double[] { bx0[r], by0[r], bx1[r], by1[r] },
                        Cx = (double)sumXR[r] / a2,
                        Cy = (double)sumYR[r] / a2,
                    });
                }
            }

            // 写标签图：下游（径向扫描/断环拼段/碎屑带）都要按 lab 取像素
            Parallel.For(0, h, y =>
            {
                int rowBase = y * w;
                for (int i = runStart[y]; i < runStart[y + 1]; i++)
                {
                    lab.AsSpan(rowBase + runX0[i], runX1[i] - runX0[i] + 1).Fill(labelOfRoot[parent[i]]);
                }
            });

            return comps;
        }
        finally
        {
            if (runCount != null) ArrayPool<int>.Shared.Return(runCount);
            if (runStart != null) ArrayPool<int>.Shared.Return(runStart);
            if (runX0 != null) ArrayPool<int>.Shared.Return(runX0);
            if (runX1 != null) ArrayPool<int>.Shared.Return(runX1);
            if (parent != null) ArrayPool<int>.Shared.Return(parent);
            if (bx0 != null) ArrayPool<int>.Shared.Return(bx0);
            if (by0 != null) ArrayPool<int>.Shared.Return(by0);
            if (bx1 != null) ArrayPool<int>.Shared.Return(bx1);
            if (by1 != null) ArrayPool<int>.Shared.Return(by1);
            if (labelOfRoot != null) ArrayPool<int>.Shared.Return(labelOfRoot);
            if (areaR != null) ArrayPool<long>.Shared.Return(areaR);
            if (sumXR != null) ArrayPool<long>.Shared.Return(sumXR);
            if (sumYR != null) ArrayPool<long>.Shared.Return(sumYR);
        }
    }

    /// <summary>逐像素扫描线填充（原实现，仅用于 A/B 对照）。</summary>
    private static List<Comp> LabelComponentsPixel(byte[] lum, int[] lab, int w, int h, double darkTh, out int nl)
    {
        var comps = new List<Comp>();
        nl = 0;
        var seedX = new int[4096];
        var seedY = new int[4096];
        for (int ys = 0; ys < h; ys++)
        {
            int rowBase0 = ys * w;
            for (int xs = 0; xs < w; xs++)
            {
                int i = rowBase0 + xs;
                if (lab[i] != 0 || lum[i] >= darkTh) continue;
                nl++;
                int sp = 0;
                seedX[sp] = xs;
                seedY[sp] = ys;
                sp++;
                int area = 0, x0 = w, y0 = h, x1 = -1, y1 = -1;
                long sx = 0, sy = 0;
                while (sp > 0)
                {
                    sp--;
                    int yy = seedY[sp], xx = seedX[sp];
                    int rb = yy * w;
                    if (lab[rb + xx] != 0) continue;
                    int xa = xx, xb = xx;
                    lab[rb + xx] = nl;
                    while (xa > 0 && lab[rb + xa - 1] == 0 && lum[rb + xa - 1] < darkTh) { xa--; lab[rb + xa] = nl; }
                    while (xb < w - 1 && lab[rb + xb + 1] == 0 && lum[rb + xb + 1] < darkTh) { xb++; lab[rb + xb] = nl; }
                    int len = xb - xa + 1;
                    area += len;
                    sx += (long)(xa + xb) * len / 2;
                    sy += (long)yy * len;
                    if (xa < x0) x0 = xa;
                    if (xb > x1) x1 = xb;
                    if (yy < y0) y0 = yy;
                    if (yy > y1) y1 = yy;
                    for (int ny = yy - 1; ny <= yy + 1; ny += 2)
                    {
                        if (ny < 0 || ny >= h) continue;
                        int nb = ny * w;
                        bool inRun = false;
                        for (int q = xa; q <= xb; q++)
                        {
                            if (lab[nb + q] == 0 && lum[nb + q] < darkTh)
                            {
                                if (!inRun)
                                {
                                    if (sp >= seedX.Length)
                                    {
                                        Array.Resize(ref seedX, seedX.Length * 2);
                                        Array.Resize(ref seedY, seedY.Length * 2);
                                    }
                                    seedX[sp] = q;
                                    seedY[sp] = ny;
                                    sp++;
                                    inRun = true;
                                }
                            }
                            else inRun = false;
                        }
                    }
                }
                comps.Add(new Comp
                {
                    Label = nl,
                    Area = area,
                    Bbox = new double[] { x0, y0, x1, y1 },
                    Cx = (double)sx / area,
                    Cy = (double)sy / area,
                });
            }
        }
        return comps;
    }

    private static DefectDto FromNotch(Notch n, string type, string typeName) => new()
    {
        Type = type,
        TypeName = typeName,
        Angle = n.Angle,
        Bbox = n.Bbox,
        X = (n.Bbox[0] + n.Bbox[2]) / 2,
        Y = (n.Bbox[1] + n.Bbox[3]) / 2,
        DepthPx = n.DepthPx,
        DepthNorm = n.DepthNorm,
        WidthDeg = n.WidthDeg,
        ArcLenPx = n.ArcLenPx,
        AngFrom = n.AngFrom,
        AngTo = n.AngTo,
    };

    private static DefectDto FromBlob(Blob b) => new()
    {
        Type = b.Type,
        TypeName = b.TypeName,
        Angle = b.Angle,
        Bbox = b.Bbox,
        X = (b.Bbox[0] + b.Bbox[2]) / 2,
        Y = (b.Bbox[1] + b.Bbox[3]) / 2,
        AreaPx = b.AreaPx,
        SizePx = b.SizePx,
        MinLum = b.MinLum,
        ReachPx = b.ReachPx,
        Elong = b.Elong,
        PcaLen = b.PcaLen,
        FreePts = b.FreePts,
    };

    private static double BoxGap(double[] a, double[] b)
    {
        double dx = Math.Max(0, Math.Max(a[0] - b[2], b[0] - a[2]));
        double dy = Math.Max(0, Math.Max(a[1] - b[3], b[1] - a[3]));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double AngToRun(Notch n, double angle)
    {
        double span = ((n.AngTo - n.AngFrom) % 360 + 360) % 360;
        double rel = ((angle - n.AngFrom) % 360 + 360) % 360;
        if (rel <= span) return 0;
        return Math.Min(rel - span, 360 - rel);
    }

    private static bool InAngleRange(double deg, double from, double to)
    {
        if (to - from >= 360) return true;
        double d = ((deg - from) % 360 + 360) % 360;
        double span = ((to - from) % 360 + 360) % 360;
        return d <= span;
    }

    private static int Mod(int a, int m)
    {
        int r = a % m;
        return r < 0 ? r + m : r;
    }

    /// <summary>JS Math.round：.5 一律向上（向 +∞）取整。</summary>
    private static int JsRound(double v) => (int)Math.Floor(v + 0.5);

    private static double Median(double[] values)
    {
        if (values.Length == 0) return 0;
        var a = (double[])values.Clone();
        Array.Sort(a);
        return a[a.Length >> 1];
    }

    private readonly record struct Circle(double Cx, double Cy, double R);

    private static bool TryFitCircle(IReadOnlyList<(double X, double Y)> pts, out Circle circle)
    {
        circle = default;
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sz = 0, sxz = 0, syz = 0;
        int n = pts.Count;
        foreach (var (x, y) in pts)
        {
            double z = x * x + y * y;
            sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y;
            sz += z; sxz += x * z; syz += y * z;
        }
        // 解 [sxx sxy sx; sxy syy sy; sx sy n] [D E F]^T = -[sxz syz sz]^T
        var A = new double[3, 3] { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
        var bv = new[] { -sxz, -syz, -sz };
        double det3(double[,] m) =>
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) -
            m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
            m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

        double d = det3(A);
        if (Math.Abs(d) < 1e-9) return false;
        var sol = new double[3];
        for (int c = 0; c < 3; c++)
        {
            var m = (double[,])A.Clone();
            for (int i = 0; i < 3; i++) m[i, c] = bv[i];
            sol[c] = det3(m) / d;
        }
        double cx = -sol[0] / 2, cy = -sol[1] / 2;
        double R = Math.Sqrt(cx * cx + cy * cy - sol[2]);
        circle = new Circle(cx, cy, R);
        return true;
    }
}
