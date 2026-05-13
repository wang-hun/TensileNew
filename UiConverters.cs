using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TensileNeW;

public sealed class BoolStringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool on = bool.TryParse(value?.ToString(), out bool result) && result;
        return on ? Brushes.LimeGreen : Brushes.LightGray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class FloatFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            float f => f.ToString("F3", culture),
            double d => d.ToString("F3", culture),
            decimal m => m.ToString("F3", culture),
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
}
