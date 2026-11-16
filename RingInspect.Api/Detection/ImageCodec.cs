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
        rgba = Array.Empty<byte>();
        width = 0;
        height = 0;
        error = null;

        if (!TryParseBase64(base64, out var bytes, out error)) return false;

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

        if (s.AsSpan().IndexOfAny('-', '_') >= 0 || s.Length % 4 != 0)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char character in s)
            {
                if (!char.IsWhiteSpace(character)) sb.Append(character);
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
}
