using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RingInspect.Api.Detection;

/// <summary>base64 图片解码：PNG / JPEG / BMP / GIF / TIFF（ImageSharp）。</summary>
public static class ImageCodec
{
    /// <summary>允许的最大像素数，防止超大图把内存撑爆。</summary>
    public const long MaxPixels = 40_000_000;

    public static bool TryDecode(string? base64, out byte[] rgba, out int width, out int height, out string? error)
    {
        if (!TryParseBase64(base64, out var bytes, out error))
        {
            rgba = Array.Empty<byte>(); width = 0; height = 0;
            return false;
        }
        return TryDecodeBytes(bytes, out rgba, out width, out height, out error);
    }

    /// <summary>调试开关：设 RINGINSPECT_NO_FASTDECODE=1 可退回 ImageSharp 通用解码（A/B 对比用）。</summary>
    private static readonly bool FastDecodeDisabled =
        Environment.GetEnvironmentVariable("RINGINSPECT_NO_FASTDECODE") == "1";

    /// <summary>从原始图片字节直接解出 RGBA（供"本地路径"接口使用，跳过 base64）。</summary>
    public static bool TryDecodeBytes(byte[] bytes, out byte[] rgba, out int width, out int height, out string? error)
    {
        // BMP 直解快速通道：24/32bpp 未压缩（BI_RGB）一次遍历写进目标数组，
        // 不经过 ImageSharp 的中间位图 —— 像素值与 ImageSharp 完全一致（BGR(A)→RGBA、按行翻转）。
        if (!FastDecodeDisabled && TryDecodeBmpFast(bytes, out rgba, out width, out height))
        {
            error = null;
            return true;
        }

        rgba = Array.Empty<byte>();
        width = 0;
        height = 0;
        error = null;

        try
        {
            var info = Image.Identify(bytes);
            if (info == null)
            {
                error = "读取失败：无法识别图片格式";
                return false;
            }
            long pixels = (long)info.Width * info.Height;
            if (info.Width <= 0 || info.Height <= 0)
            {
                error = "读取失败：图片尺寸无效";
                return false;
            }
            if (pixels > MaxPixels)
            {
                error = $"读取失败：图片像素过多（{info.Width}×{info.Height}，上限 {MaxPixels} 像素）";
                return false;
            }

            using var image = Image.Load<Rgba32>(bytes);
            width = image.Width;
            height = image.Height;
            rgba = new byte[(long)width * height * 4];
            image.CopyPixelDataTo(rgba);
            return true;
        }
        catch (Exception ex)
        {
            rgba = Array.Empty<byte>();
            width = 0;
            height = 0;
            error = "读取失败：" + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// BMP 快速解码：只处理 24/32bpp、未压缩（BI_RGB）的常规位图（工业相机导出就是这种）。
    /// 直接按行写进目标 RGBA 数组，省掉 ImageSharp 的中间位图与二次拷贝；
    /// 像素值与原解码逐字节一致（BGR→RGB 交换、24bpp 补 A=255、bottom-up 翻转、行 4 字节对齐）。
    /// 任何一个条件不满足就返回 false，交给 ImageSharp 走通用流程（PNG/JPEG/压缩 BMP 不受影响）。
    /// </summary>
    private static bool TryDecodeBmpFast(byte[] bytes, out byte[] rgba, out int width, out int height)
    {
        rgba = Array.Empty<byte>();
        width = 0;
        height = 0;

        var span = bytes.AsSpan();
        if (span.Length < 54) return false;
        if (span[0] != (byte)'B' || span[1] != (byte)'M') return false;

        int offBits = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(10));
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(14));
        if (headerSize < 40) return false;

        int w = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(18));
        int hRaw = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(22));
        int planes = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(26));
        int bpp = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(28));
        int comp = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(30));

        if (planes != 1) return false;
        if (bpp != 24 && bpp != 32) return false;
        if (comp != 0) return false;                  // 只吃 BI_RGB；BI_BITFIELDS / RLE 交给通用流程

        bool topDown = hRaw < 0;
        int h = hRaw < 0 ? -hRaw : hRaw;
        if (w <= 0 || h <= 0) return false;
        if ((long)w * h > MaxPixels) return false;

        int bytesPerPixel = bpp / 8;
        int stride = (w * bytesPerPixel + 3) & ~3;    // BMP 每行按 4 字节对齐
        long need = (long)offBits + (long)stride * h;
        if (offBits < 0 || need > span.Length) return false;

        // 整块都会被写满，用未清零分配：48MB 的图省掉一次清零（约 5ms 的缺页/填零）。
        var dst = GC.AllocateUninitializedArray<byte>((int)((long)w * h * 4));
        if (bpp == 24)
        {
            // 大图按行分块并行：各块只写 dst 里自己那段区间，没有共享状态，结果与单线程逐字节相同。
            // （单线程标量搬运 12MP 约 35ms，8 核并行后约 5ms，大图解码不再是瓶颈。）
            if ((long)w * h >= 1_000_000)
            {
                int chunks = Math.Min(h, Math.Max(1, Environment.ProcessorCount));
                int per = (h + chunks - 1) / chunks;
                Parallel.For(0, chunks, c =>
                {
                    int y0 = c * per;
                    int y1 = Math.Min(h, y0 + per);
                    Decode24Rows(bytes, dst, w, h, stride, offBits, topDown, y0, y1);
                });
            }
            else
            {
                Decode24Rows(bytes, dst, w, h, stride, offBits, topDown, 0, h);
            }
        }
        else
        {
            byte alphaOr = 0;
            for (int y = 0; y < h; y++)
            {
                int src = offBits + (topDown ? y : h - 1 - y) * stride;
                int d = y * w * 4;
                int end = d + w * 4;
                while (d < end)
                {
                    dst[d] = span[src + 2];
                    dst[d + 1] = span[src + 1];
                    dst[d + 2] = span[src];
                    dst[d + 3] = span[src + 3];   // 32bpp BI_RGB：第 4 字节原样透传
                    alphaOr |= span[src + 3];
                    d += 4;
                    src += 4;
                }
            }
            // 与 ImageSharp 对齐：整图 alpha 全 0 时视为不透明（相机导出的 32bpp 常把 alpha 填 0）
            if (alphaOr == 0)
            {
                for (int i = 3; i < dst.Length; i += 4) dst[i] = 255;
            }
        }

        rgba = dst;
        width = w;
        height = h;
        return true;
    }

    /// <summary>把 24bpp BMP 的第 [y0,y1) 行翻译成 RGBA 写进 dst（纯搬运，无共享状态，可并行）。</summary>
    private static void Decode24Rows(byte[] src, byte[] dst, int w, int h, int stride, int offBits,
        bool topDown, int y0, int y1)
    {
        for (int y = y0; y < y1; y++)
        {
            int s = offBits + (topDown ? y : h - 1 - y) * stride;
            int d = y * w * 4;
            int end = d + w * 4;
            while (d < end)
            {
                dst[d] = src[s + 2];
                dst[d + 1] = src[s + 1];
                dst[d + 2] = src[s];
                dst[d + 3] = 255;
                d += 4;
                s += 3;
            }
        }
    }

    public static bool TryParseBase64(string? input, out byte[] bytes, out string? error)
    {
        bytes = Array.Empty<byte>();
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "缺少图片数据（image 字段，base64）";
            return false;
        }

        var s = input.Trim();
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = s.IndexOf(',');
            if (comma < 0)
            {
                error = "base64 的 data URL 格式不正确";
                return false;
            }
            s = s[(comma + 1)..];
        }

        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c)) continue;
            sb.Append(c);
        }
        s = sb.ToString().Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1:
                error = "base64 长度不合法";
                return false;
        }

        try
        {
            bytes = Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            error = "base64 解码失败，请检查图片数据";
            return false;
        }

        if (bytes.Length == 0)
        {
            error = "图片数据为空";
            return false;
        }
        return true;
    }

    public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>
    /// 把已解码的 RGBA 编码成 JPEG data URL（全尺寸，坐标与检测结果天然对齐）。
    /// 用于路径模式给前端提供轻量底图，替代昂贵的 overlay。
    /// </summary>
    public static string? ToJpegDataUrl(byte[] rgba, int w, int h, int quality = 82)
    {
        try
        {
            using var image = Image.LoadPixelData<Rgba32>(rgba, w, h);
            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = quality });
            return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }
}
