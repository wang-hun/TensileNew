using System.Diagnostics;

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
        int w = width, h = height, N = w * h;
        if (rgba.Length < (long)N * 4) throw new ArgumentException("RGBA 数据长度不足");

        // max(R,G,B) 保持彩色标注/黄色治具为亮；debLum 用标准灰度，能抓到很淡的紫边毛刺，同时忽略纯红标记笔。
        var lum = new byte[N];
        var debLum = new byte[N];
        for (int i = 0, j = 0; i < N; i++, j += 4)
        {
            int r8 = rgba[j], g8 = rgba[j + 1], b8 = rgba[j + 2];
            lum[i] = (byte)(r8 > g8 ? (r8 > b8 ? r8 : b8) : (g8 > b8 ? g8 : b8));
            bool isRedPen = r8 > 150 && r8 - g8 > 60 && r8 - b8 > 60;
            debLum[i] = isRedPen ? (byte)255 : (byte)JsRound(r8 * 0.299 + g8 * 0.587 + b8 * 0.114);
        }

        var lab = new int[N];
        var stack = new int[N];
        var comps = new List<Comp>();
        int nl = 0;
        for (int i = 0; i < N; i++)
        {
            if (lab[i] != 0 || lum[i] >= p.DarkTh) continue;
            nl++;
            int sp = 0;
            stack[sp++] = i;
            lab[i] = nl;
            int area = 0, x0 = w, y0 = h, x1 = -1, y1 = -1;
            long sx = 0, sy = 0;
            while (sp > 0)
            {
                int q = stack[--sp];
                int x = q % w;
                int y = (q - x) / w;
                area++;
                sx += x;
                sy += y;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
                if (x > 0)
                {
                    int n = q - 1;
                    if (lab[n] == 0 && lum[n] < p.DarkTh) { lab[n] = nl; stack[sp++] = n; }
                }
                if (x < w - 1)
                {
                    int n = q + 1;
                    if (lab[n] == 0 && lum[n] < p.DarkTh) { lab[n] = nl; stack[sp++] = n; }
                }
                if (y > 0)
                {
                    int n = q - w;
                    if (lab[n] == 0 && lum[n] < p.DarkTh) { lab[n] = nl; stack[sp++] = n; }
                }
                if (y < h - 1)
                {
                    int n = q + w;
                    if (lab[n] == 0 && lum[n] < p.DarkTh) { lab[n] = nl; stack[sp++] = n; }
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
        if (ring == null)
        {
            return DetectResult.Failure("NO_RING", ErrNoRing, sw.Elapsed.TotalMilliseconds);
        }

        int RL = ring.Label;

        double[] Scan(double cx, double cy)
        {
            double x0 = ring.Bbox[0], y0 = ring.Bbox[1], x1 = ring.Bbox[2], y1 = ring.Bbox[3];
            double tMax = 0;
            foreach (var (x, y) in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) })
            {
                tMax = Math.Max(tMax, Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)));
            }
            tMax += 3;
            var rad = new double[Na];
            Array.Fill(rad, -1.0);
            for (int k = 0; k < Na; k++)
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
                    if (lab[q] == RL)
                    {
                        int v0 = lum[q];
                        double f = prevLum > v0 ? (prevLum - p.DarkTh) / (prevLum - v0) : 0;
                        rad[k] = t + 0.5 * Math.Min(1, Math.Max(0, f));
                        break;
                    }
                    prevLum = lum[q];
                }
            }
            return rad;
        }

        double cx0 = ring.Cx, cy0 = ring.Cy;
        double[] r = Scan(cx0, cy0);
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

        // 碎屑/毛刺：检测带锚定在局部本体边缘 rBody[k] 上（尖刺长在凹口里也落在带内）。
        double bMin = double.PositiveInfinity, bMax = 0;
        for (int k = 0; k < Na; k++)
        {
            bMin = Math.Min(bMin, rBody[k]);
            bMax = Math.Max(bMax, rBody[k]);
        }
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
            var hit = new byte[BW * BH];
            for (int y = bbY0; y <= bbY1; y++)
            {
                double dy = y - cy0;
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
                    bandPixels++;
                    if (debLum[y * w + x] < p.DebrisTh) hit[(y - bbY0) * BW + (x - bbX0)] = 1;
                }
            }
            var seen = new byte[BW * BH];
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

        // 缺口与附近异物配对；附近没有异物的缺口只算候选。
        double pairDist = p.PairDist * s;
        double angSpan = Math.Max(8, pairDist / circle.R * 180 / Math.PI * 1.5);
        var usedN = new bool[notches.Count];
        var usedB = new bool[blobs.Count];
        var byNotch = new List<List<(Blob Blob, double GapPx)>>();
        for (int i = 0; i < notches.Count; i++) byNotch.Add(new List<(Blob, double)>());

        var defects = new List<DefectDto>();
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

        var ordered = defects.OrderBy(d => d.Angle).ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].Id = i + 1;

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
            Ring = new RingDto { Area = ring.Area, Bbox = ring.Bbox },
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
        return result;
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
