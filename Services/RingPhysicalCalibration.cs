using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using NLog;

namespace TensileNeW.Services;

public sealed record RingPhysicalDimensions(double DiameterMillimeters, double HeightMillimeters);

public sealed class RingPhysicalCalibration
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public const string FileName = "dice-calibration.json";
    private const string SchemaName = "dice-calibration";
    private const double MinimumDenominatorRatio = 1e-12;
    private const double MinimumNumeratorRatio = 1e-12;
    private const int MaximumCalibrationFileBytes = 1024 * 1024;

    private RingPhysicalCalibration(double m, double d10, double alphaDegrees, double h)
    {
        M = m;
        D10 = d10;
        H = h;
        CotAlpha = 1d / Math.Tan(alphaDegrees * Math.PI / 180d);
        if (!double.IsFinite(CotAlpha) || CotAlpha <= 0d)
            throw new InvalidDataException("alpha 无法计算有效的 cot 值。");
    }

    public double M { get; }
    public double D10 { get; }
    public double H { get; }
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
        ValidateUnit(root, "mUnit", "pixel/mm");
        ValidateUnit(root, "M10Unit", "mm");
        ValidateUnit(root, "alphaUnit", "deg");
        ValidateUnit(root, "HUnit", "mm");
        if (alpha >= 90d) throw new InvalidDataException("alpha 必须大于 0 且小于 90 度。");
        return new RingPhysicalCalibration(m, d10, alpha, h);
    }

    public RingPhysicalDimensions Calculate(double z10, double pixelDiameter)
    {
        if (!double.IsFinite(z10)) throw new ArgumentOutOfRangeException(nameof(z10), "停机位移不是有效数值。");
        if (!double.IsFinite(pixelDiameter) || pixelDiameter <= 0d)
            throw new ArgumentOutOfRangeException(nameof(pixelDiameter), "像素直径必须是正的有限数值。");

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
        double height = z10 + (D10 - diameter) / (2d * CotAlpha);
        if (!double.IsFinite(diameter) || diameter <= 0d || !double.IsFinite(height))
            throw new InvalidDataException("计算得到的物理尺寸无效，请检查停机位移、像素直径及标定参数。");
        return new RingPhysicalDimensions(diameter, height);
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
