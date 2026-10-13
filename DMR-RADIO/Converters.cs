using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DMR_RADIO.Converters;

/// <summary>
/// bool → štětec. Hodnota true vrátí barvu z ConverterParameter (např. "#3DDC84"),
/// false vrátí tmavou "zhasnutou" barvu. Používá se pro LED indikátory.
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    private static readonly Brush Off = Create(Color.FromRgb(0x2A, 0x30, 0x3B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string hex && ColorConverter.ConvertFromString(hex) is Color color)
            return Create(color);

        return Off;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush Create(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}