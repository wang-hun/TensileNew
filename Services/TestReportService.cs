using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Xceed.Document.NET;
using Xceed.Words.NET;

namespace TensileNeW.Services;

public static class TestReportService
{
    public static void Save(
        string fileName,
        string imagePath,
        string recipeName,
        string trialSerialNumber,
        DateTime generatedAt,
        string maxForce,
        string validDistance)
    {
        using var document = DocX.Create(fileName);
        document.Sections[0].PageLayout.Orientation = Orientation.Portrait;

        var title = document.InsertParagraph("试验报告");
        title.Alignment = Alignment.center;
        title.FontSize(18).Bold();

        var info = document.InsertParagraph(
            $"试验名称：{recipeName}    试验序列号：{trialSerialNumber}    生成时间：{generatedAt:yyyy-MM-dd HH:mm:ss}");
        info.Alignment = Alignment.center;
        info.FontSize(11);

        var picture = document.AddImage(imagePath).CreatePicture();
        FitPictureToPortraitPage(picture, imagePath);
        var pictureParagraph = document.InsertParagraph();
        pictureParagraph.Alignment = Alignment.center;
        pictureParagraph.AppendPicture(picture);

        var maxForceParagraph = document.InsertParagraph($"最大拉伸力：{maxForce}");
        maxForceParagraph.FontSize(12);
        var validDistanceParagraph = document.InsertParagraph($"有效拉伸位移：{validDistance}");
        validDistanceParagraph.FontSize(12);
        document.Save();
    }

    public static string SaveClipboardImageToTempFile()
    {
        BitmapSource? clipboardImage = Clipboard.GetImage();
        if (clipboardImage == null)
        {
            throw new InvalidOperationException("无法从剪贴板获取曲线图图片");
        }

        string tempImagePath = Path.Combine(Path.GetTempPath(), $"TensileReport_{Guid.NewGuid():N}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(clipboardImage));
        using var stream = File.Create(tempImagePath);
        encoder.Save(stream);
        return tempImagePath;
    }

    private static void FitPictureToPortraitPage(Picture picture, string imagePath)
    {
        const int maxWidth = 500;
        const int maxHeight = 330;

        using var stream = File.OpenRead(imagePath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        int sourceWidth = decoder.Frames[0].PixelWidth;
        int sourceHeight = decoder.Frames[0].PixelHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return;
        }

        double scale = Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight);
        scale = Math.Min(scale, 1.0);
        picture.Width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        picture.Height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
    }
}
