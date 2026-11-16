using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RingInspect.Api.Detection;

/// <summary>
/// 把检测结果画到原图上，输出与网页画布同风格的 PNG（拟合圆/检测带/缺陷框/编号）。
/// 使用纯像素绘制，不依赖任何绘图库。
/// </summary>
public static class OverlayRenderer
{
    private static readonly Rgba32 CircleColor = new(26, 127, 55, 230);
    private static readonly Rgba32 BandColor = new(9, 105, 218, 20);
    private static readonly Rgba32 BandLine = new(9, 105, 218, 110);
    private static readonly Rgba32 EdgeColor = new(255, 212, 59, 255);
    private static readonly Rgba32 CandidateColor = new(140, 149, 159, 190);
    private static readonly Rgba32 NotchColor = new(232, 89, 12, 255);
    private static readonly Rgba32 DebrisColor = new(214, 51, 108, 255);

    private static readonly Dictionary<string, Rgba32> TypeColors = new()
    {
        ["paired"] = new Rgba32(207, 34, 46, 255),
        ["bignotch"] = new Rgba32(207, 34, 46, 255),
        ["notch"] = new Rgba32(232, 89, 12, 255),
        ["debris"] = new Rgba32(214, 51, 108, 255),
        ["burr"] = new Rgba32(112, 72, 232, 255),
    };

    public static string? Render(byte[] rgba, int w, int h, DetectResult result,
        double[] r, double[] rBody, double cx, double cy, double R)
    {
        try
        {
            int scale = Math.Max(1, (int)Math.Round(R / 420.0));
            var canvas = new byte[rgba.Length];
            Buffer.BlockCopy(rgba, 0, canvas, 0, rgba.Length);

            // 检测带（半透明填充）
            int gapPx = (int)Math.Round(result.Band?.Gap ?? 0);
            int bandPx = (int)Math.Round(result.Band?.Band ?? 0);
            if (gapPx >= 0 && bandPx > 0)
            {
                int x0 = Math.Max(0, (int)Math.Floor(cx - R - bandPx - gapPx - 4));
                int x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + R + bandPx + gapPx + 4));
                int y0 = Math.Max(0, (int)Math.Floor(cy - R - bandPx - gapPx - 4));
                int y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + R + bandPx + gapPx + 4));
                int n = rBody.Length;
                for (int y = y0; y <= y1; y++)
                {
                    double dy = y - cy;
                    for (int x = x0; x <= x1; x++)
                    {
                        double dx = x - cx;
                        double rr = Math.Sqrt(dx * dx + dy * dy);
                        double a = Math.Atan2(dy, dx);
                        if (a < 0) a += 2 * Math.PI;
                        int k = (int)Math.Floor(a / (2 * Math.PI) * n + 0.5) % n;
                        double lo = rBody[k] + gapPx;
                        if (rr < lo || rr > lo + bandPx) continue;
                        Blend(canvas, (y * w + x) * 4, BandColor);
                    }
                }
            }

            // 拟合圆
            DrawCircle(canvas, w, h, cx, cy, R, CircleColor, Math.Max(1, scale));
            // 圆心十字
            int m = 10;
            int icx = (int)Math.Round(cx), icy = (int)Math.Round(cy);
            DrawLineRaw(canvas, w, h, icx - m, icy, icx + m, icy, CircleColor);
            DrawLineRaw(canvas, w, h, icx, icy - m, icx, icy + m, CircleColor);

            // 外缘轮廓（黄色折线）
            int prevX = int.MinValue, prevY = int.MinValue;
            for (int k = 0; k <= r.Length; k++)
            {
                int kk = k % r.Length;
                double t = r[kk];
                if (t <= 0) { prevX = int.MinValue; continue; }
                double a = (double)kk / r.Length * 2 * Math.PI;
                int x = (int)Math.Round(cx + Math.Cos(a) * t);
                int y = (int)Math.Round(cy + Math.Sin(a) * t);
                if (prevX != int.MinValue) DrawLineRaw(canvas, w, h, prevX, prevY, x, y, EdgeColor);
                prevX = x;
                prevY = y;
            }

            // 已忽略的缺口/异物（灰色虚线框）
            if (result.Candidates != null)
            {
                foreach (var d in result.Candidates)
                {
                    DrawRectDashed(canvas, w, h, d.Bbox, 4, CandidateColor, 4, 3, 1);
                }
            }

            // 缺陷框
            if (result.Defects != null)
            {
                foreach (var d in result.Defects)
                {
                    var color = TypeColors.TryGetValue(d.Type, out var c) ? c : new Rgba32(207, 34, 46, 255);
                    int lw = Math.Max(2, scale * 2);
                    if (d.NotchBox != null)
                    {
                        DrawRectDashed(canvas, w, h, d.NotchBox, 3, NotchColor, 4, 3, lw);
                        if (d.DebrisBoxes != null)
                        {
                            foreach (var b in d.DebrisBoxes) DrawRectDashed(canvas, w, h, b, 3, DebrisColor, 4, 3, lw);
                        }
                        else if (d.DebrisBox != null)
                        {
                            DrawRectDashed(canvas, w, h, d.DebrisBox, 3, DebrisColor, 4, 3, lw);
                        }
                    }
                    DrawRect(canvas, w, h, d.Bbox, 6, color, lw);
                    DrawTag(canvas, w, h, (int)Math.Round(d.Bbox[0]) - 6, (int)Math.Round(d.Bbox[1]) - 6,
                        "#" + (d.Id ?? 0), color, scale);
                }
            }

            using var image = Image.LoadPixelData<Rgba32>(canvas, w, h);
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static void Blend(byte[] canvas, int i, Rgba32 c)
    {
        double a = c.A / 255.0;
        canvas[i] = (byte)Math.Round(canvas[i] * (1 - a) + c.R * a);
        canvas[i + 1] = (byte)Math.Round(canvas[i + 1] * (1 - a) + c.G * a);
        canvas[i + 2] = (byte)Math.Round(canvas[i + 2] * (1 - a) + c.B * a);
        canvas[i + 3] = 255;
    }

    private static void SetPixel(byte[] canvas, int w, int h, int x, int y, Rgba32 c)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        Blend(canvas, (y * w + x) * 4, c);
    }

    private static void DrawLineRaw(byte[] canvas, int w, int h, int x0, int y0, int x1, int y1, Rgba32 c)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int guard = 0;
        while (true)
        {
            SetPixel(canvas, w, h, x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            if (++guard > 4 * (w + h)) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void DrawCircle(byte[] canvas, int w, int h, double cx, double cy, double R, Rgba32 c, int width)
    {
        int x0 = Math.Max(0, (int)Math.Floor(cx - R - width));
        int x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + R + width));
        int y0 = Math.Max(0, (int)Math.Floor(cy - R - width));
        int y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + R + width));
        double half = width / 2.0 + 0.2;
        for (int y = y0; y <= y1; y++)
        {
            double dy = y - cy;
            for (int x = x0; x <= x1; x++)
            {
                double dx = x - cx;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (Math.Abs(d - R) <= half) Blend(canvas, (y * w + x) * 4, c);
            }
        }
    }

    private static void DrawRect(byte[] canvas, int w, int h, double[] bbox, int pad, Rgba32 c, int width)
    {
        int x0 = (int)Math.Round(bbox[0]) - pad, y0 = (int)Math.Round(bbox[1]) - pad;
        int x1 = (int)Math.Round(bbox[2]) + pad, y1 = (int)Math.Round(bbox[3]) + pad;
        for (int i = 0; i < width; i++)
        {
            DrawLineRaw(canvas, w, h, x0 + i, y0 + i, x1 - i, y0 + i, c);
            DrawLineRaw(canvas, w, h, x1 - i, y0 + i, x1 - i, y1 - i, c);
            DrawLineRaw(canvas, w, h, x1 - i, y1 - i, x0 + i, y1 - i, c);
            DrawLineRaw(canvas, w, h, x0 + i, y1 - i, x0 + i, y0 + i, c);
        }
    }

    private static void DrawRectDashed(byte[] canvas, int w, int h, double[] bbox, int pad, Rgba32 c, int dash, int space, int width)
    {
        int x0 = (int)Math.Round(bbox[0]) - pad, y0 = (int)Math.Round(bbox[1]) - pad;
        int x1 = (int)Math.Round(bbox[2]) + pad, y1 = (int)Math.Round(bbox[3]) + pad;
        for (int i = 0; i < width; i++)
        {
            DrawDashedLine(canvas, w, h, x0 + i, y0 + i, x1 - i, y0 + i, c, dash, space);
            DrawDashedLine(canvas, w, h, x1 - i, y0 + i, x1 - i, y1 - i, c, dash, space);
            DrawDashedLine(canvas, w, h, x1 - i, y1 - i, x0 + i, y1 - i, c, dash, space);
            DrawDashedLine(canvas, w, h, x0 + i, y1 - i, x0 + i, y0 + i, c, dash, space);
        }
    }

    private static void DrawDashedLine(byte[] canvas, int w, int h, int x0, int y0, int x1, int y1, Rgba32 c, int dash, int space)
    {
        int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        for (int i = 0; i <= steps; i++)
        {
            if ((i / dash) % 2 == 1 && space > 0) continue;
            double t = steps == 0 ? 0 : (double)i / steps;
            int x = (int)Math.Round(x0 + (x1 - x0) * t);
            int y = (int)Math.Round(y0 + (y1 - y0) * t);
            SetPixel(canvas, w, h, x, y, c);
        }
    }

    private static void DrawTag(byte[] canvas, int w, int h, int x, int y, string text, Rgba32 bg, int scale)
    {
        int gw = 6 * scale, gh = 9 * scale;
        int tw = text.Length * gw + 6 * scale;
        int th = gh + 4 * scale;
        int bx = x - 2 * scale, by = y - th;
        for (int j = 0; j < th; j++)
        {
            for (int i = 0; i < tw; i++)
            {
                SetPixel(canvas, w, h, bx + i, by + j, bg);
            }
        }
        for (int ci = 0; ci < text.Length; ci++)
        {
            var glyph = Font5x7.Get(text[ci]);
            if (glyph == null) continue;
            int gx = bx + 3 * scale + ci * gw;
            int gy = by + 2 * scale;
            for (int row = 0; row < 7; row++)
            {
                string bits = glyph[row];
                for (int col = 0; col < 5; col++)
                {
                    if (bits[col] != '1') continue;
                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            SetPixel(canvas, w, h, gx + col * scale + sx, gy + row * scale + sy, new Rgba32(255, 255, 255, 255));
                        }
                    }
                }
            }
        }
    }

    private static class Font5x7
    {
        private static readonly Dictionary<char, string[]> Glyphs = new()
        {
            ['#'] = new[] { "01010", "11111", "01010", "01010", "11111", "01010", "00000" },
            ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
            ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
            ['2'] = new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
            ['3'] = new[] { "11111", "00010", "00100", "00010", "00001", "10001", "01110" },
            ['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
            ['5'] = new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
            ['6'] = new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
            ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
            ['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
            ['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
        };

        public static string[]? Get(char c) => Glyphs.TryGetValue(c, out var g) ? g : null;
    }
}
