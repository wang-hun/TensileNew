using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using NLog;

namespace TensileNeW.Services;

public sealed record RingPixelRadii(double InnerRadius, double OuterRadius);

public sealed record RingPhysicalDimensions(double OuterDiameterMillimeters, double InnerDiameterMillimeters, double HeightMillimeters);
public sealed record RingReference2Diameters(double OuterDiameterMillimeters, double InnerDiameterMillimeters);

public sealed class RingPhysicalCalibration
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public const string FileName = "dice-calibration.json";
    private const string SchemaName = "dice-calibration";
    private const double MinimumDenominatorRatio = 1e-12;
    private const double MinimumNumeratorRatio = 1e-12;
    private const int MaximumCalibrationFileBytes = 1024 * 1024;

    private RingPhysicalCalibration(double m, double d10, double alphaDegrees, double h, double? dt, double? focalLengthMillimeters, double? pixelSizeMicrometers)
    {
        M = m;
        D10 = d10;
        H = h;
        Dt = dt;
        FocalLengthMillimeters = focalLengthMillimeters;
        PixelSizeMicrometers = pixelSizeMicrometers;
        CotAlpha = 1d / Math.Tan(alphaDegrees * Math.PI / 180d);
        if (!double.IsFinite(CotAlpha) || CotAlpha <= 0d)
            throw new InvalidDataException("alpha 无法计算有效的 cot 值。");
    }

    public double M { get; }
    public double D10 { get; }
    public double H { get; }
    public double? Dt { get; }
    public double? FocalLengthMillimeters { get; }
    public double? PixelSizeMicrometers { get; }
    public double CotAlpha { get; }
    public static RingPhysicalCalibration? Current { get; private set; }
    public static string? LoadError { get; private set; }
    public static void Initialize()
    {
        try
        {
            bool isDebugging = Debugger.IsAttached;
            string? path = isDebugging ? SelectDebugCalibrationFile() : Path.Combine(AppContext.BaseDirectory, FileName);
            if (path is null)
            {
                Current = null;
                LoadError = "未选择标定文件。";
                Logger.Warn(LoadError);
                return;
            }

            string runtimePath = Path.Combine(AppContext.BaseDirectory, FileName);
            if (isDebugging && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(runtimePath), StringComparison.OrdinalIgnoreCase))
                File.Copy(path, runtimePath, overwrite: true);
            Current = Load(runtimePath);
            LoadError = null;
            Logger.Info("已加载物理尺寸标定文件：{0}", runtimePath);
        }
        catch (Exception exception)
        {
            Current = null;
            LoadError = $"标定文件加载失败：{exception.Message}";
            Logger.Error(exception, LoadError);
        }
    }

    public static RingPhysicalCalibration Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        if (stream.Length > MaximumCalibrationFileBytes)
            throw new InvalidDataException("标定文件超过 1 MB，无法加载。");
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out JsonElement schema) || schema.ValueKind != JsonValueKind.String || schema.GetString() != SchemaName)
            throw new InvalidDataException("schema 必须为 dice-calibration。");
        if (!root.TryGetProperty("schemaVersion", out JsonElement schemaVersion) || schemaVersion.ValueKind != JsonValueKind.Number || !schemaVersion.TryGetInt32(out int version) || version != 2)
            throw new InvalidDataException("仅支持 schemaVersion 2 的标定文件。");

        double m = ReadPositiveNumber(root, "m");
        double d10 = ReadPositiveNumber(root, "M10");
        double alpha = ReadPositiveNumber(root, "alpha");
        double h = ReadPositiveNumber(root, "H");
        double? dt = ReadOptionalPositiveNumber(root, "Dt");
        double? focalLengthMillimeters = ReadOptionalPositiveNumber(root, "F");
        double? pixelSizeMicrometers = ReadOptionalPositiveNumber(root, "s");
        ValidateUnit(root, "mUnit", "pixel/mm");
        ValidateUnit(root, "M10Unit", "mm");
        ValidateUnit(root, "alphaUnit", "deg");
        ValidateUnit(root, "HUnit", "mm");
        if (alpha >= 90d) throw new InvalidDataException("alpha 必须大于 0 且小于 90 度。");
        return new RingPhysicalCalibration(m, d10, alpha, h, dt, focalLengthMillimeters, pixelSizeMicrometers);
    }

    public RingPhysicalDimensions Calculate(double z10, double pixelInnerRadius, double pixelOuterRadius)
    {
        if (!double.IsFinite(z10)) throw new ArgumentOutOfRangeException(nameof(z10), "停机位移不是有效数值。");
        if (!double.IsFinite(pixelInnerRadius) || pixelInnerRadius <= 0d)
            throw new ArgumentOutOfRangeException(nameof(pixelInnerRadius), "像素内圆半径必须是正的有限数值。");
        if (!double.IsFinite(pixelOuterRadius) || pixelOuterRadius <= 0d)
            throw new ArgumentOutOfRangeException(nameof(pixelOuterRadius), "像素外圆半径必须是正的有限数值。");

        double innerDiameter = CalculateDiameter(z10, pixelInnerRadius * 2d);
        double outerDiameter = CalculateDiameter(z10, pixelOuterRadius * 2d);
        double height = z10 + (D10 - innerDiameter) / (2d * CotAlpha);
        if (!double.IsFinite(height))
            throw new InvalidDataException("计算得到的物理尺寸无效，请检查停机位移、像素半径及标定参数。");
        return new RingPhysicalDimensions(outerDiameter, innerDiameter, height);
    }

    public RingReference2Diameters CalculateReference2(double pixelHoleRadius, double pixelInnerRadius, double pixelOuterRadius)
    {
        if (Dt is null || FocalLengthMillimeters is null || PixelSizeMicrometers is null)
            throw new InvalidDataException("参考值2需要标定文件提供 Dt（mm）、F（mm）和 s（μm）。");
        if (!double.IsFinite(pixelHoleRadius) || pixelHoleRadius <= 0d)
            throw new ArgumentOutOfRangeException(nameof(pixelHoleRadius), "圆台顶面中心孔像素半径必须是正的有限数值。");

        double pixelFocalLength = FocalLengthMillimeters.Value * 1000d / PixelSizeMicrometers.Value;
        double topPixelsPerMillimeter = pixelHoleRadius * 2d / Dt.Value;
        if (!double.IsFinite(pixelFocalLength) || !double.IsFinite(topPixelsPerMillimeter) || topPixelsPerMillimeter <= 0d)
            throw new InvalidDataException("参考值2的像素焦距或圆台顶面像素比例无效。");

        double topCorrection = 2d * CotAlpha * pixelFocalLength / topPixelsPerMillimeter;
        double pixelCorrection = 2d * CotAlpha * pixelFocalLength;
        double diameterFactor = Dt.Value - topCorrection;
        double outerDiameter = CalculateReference2Diameter(pixelOuterRadius * 2d, pixelCorrection, diameterFactor, Dt.Value, topCorrection);
        double innerDiameter = CalculateReference2Diameter(pixelInnerRadius * 2d, pixelCorrection, diameterFactor, Dt.Value, topCorrection);
        return new RingReference2Diameters(outerDiameter, innerDiameter);
    }

    private static double CalculateReference2Diameter(double pixelDiameter, double pixelCorrection, double diameterFactor, double dt, double topCorrection)
    {
        if (!double.IsFinite(pixelDiameter) || pixelDiameter <= 0d)
            throw new InvalidDataException("参考值2的像素直径必须是正的有限数值。");

        double denominator = pixelDiameter - pixelCorrection;
        double denominatorScale = Math.Max(Math.Abs(pixelDiameter), Math.Abs(pixelCorrection));
        double factorScale = Math.Max(Math.Abs(dt), Math.Abs(topCorrection));
        double diameter = pixelDiameter * diameterFactor / denominator;
        if (!double.IsFinite(diameterFactor) || !double.IsFinite(denominator) ||
            Math.Abs(diameterFactor) <= factorScale * MinimumNumeratorRatio ||
            Math.Abs(denominator) <= denominatorScale * MinimumDenominatorRatio ||
            !double.IsFinite(diameter) || diameter <= 0d)
            throw new InvalidDataException("参考值2直径公式的参数抵消、分母接近零或结果无效。");
        return diameter;
    }

    private double CalculateDiameter(double z10, double pixelDiameter)
    {
        double diameterFactor = 2d * (H - z10) * CotAlpha - D10;
        double denominatorTerm = 2d * M * H * CotAlpha;
        double denominator = denominatorTerm - pixelDiameter;
        double numerator = pixelDiameter * diameterFactor;
        double denominatorScale = Math.Max(Math.Abs(denominatorTerm), Math.Abs(pixelDiameter));
        double factorScale = Math.Max(Math.Abs(2d * (H - z10) * CotAlpha), D10);
        if (!double.IsFinite(numerator) || !double.IsFinite(denominator) || !double.IsFinite(factorScale) ||
            Math.Abs(denominator) <= denominatorScale * MinimumDenominatorRatio ||
            Math.Abs(diameterFactor) <= factorScale * MinimumNumeratorRatio)
            throw new InvalidDataException("物理直径公式的参数相互抵消或分母接近零，无法可靠计算。");

        double diameter = numerator / denominator;
        if (!double.IsFinite(diameter) || diameter <= 0d)
            throw new InvalidDataException("计算得到的物理直径无效，请检查停机位移、像素直径及标定参数。");
        return diameter;
    }

    public static string? SelectDebugCalibrationFile()
    {
        try
        {
            if (GetConsoleWindow() != IntPtr.Zero || AllocConsole())
            {
                Console.OutputEncoding = new System.Text.UTF8Encoding(false);
                Console.WriteLine("请选择弹出窗口中的物理尺寸标定 JSON 文件；取消则本次调试不计算物理尺寸。");
            }
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "无法显示调试控制台提示，仍将打开标定文件选择窗口。");
        }

        return SelectCalibrationFile();
    }

    public static string? SelectCalibrationFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择物理尺寸标定文件",
            Filter = "标定 JSON 文件 (*.json)|*.json",
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true,
            RestoreDirectory = true
        };

        dialog.FileOk += (_, eventArgs) =>
        {
            try
            {
                _ = Load(dialog.FileName);
            }
            catch (Exception exception)
            {
                eventArgs.Cancel = true;
                Logger.Warn(exception, "所选标定文件无效或无法读取：{0}", dialog.FileName);
                System.Windows.MessageBox.Show(
                    $"所选标定文件无效或无法读取：{exception.Message}\n\n请重新选择标定 JSON 文件，或点击“取消”。",
                    "标定文件错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        };

        return dialog.ShowDialog() == true ? Path.GetFullPath(dialog.FileName) : null;
    }

    private static double ReadPositiveNumber(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out double number) || !double.IsFinite(number) || number <= 0d)
            throw new InvalidDataException($"{propertyName} 必须是正的有限数值。");
        return number;
    }

    private static double? ReadOptionalPositiveNumber(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out _) ? ReadPositiveNumber(root, propertyName) : null;

    private static void ValidateUnit(JsonElement root, string propertyName, string expectedValue)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.String || value.GetString() != expectedValue)
            throw new InvalidDataException($"{propertyName} 必须为 {expectedValue}。");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
