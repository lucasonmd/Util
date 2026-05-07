using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RuntimeMonitor.Viewer.Converters;

/// <summary>
/// 타입 이름에 따라 색상을 반환하는 컨버터
/// ListView에서 타입을 시각적으로 구분할 때 사용
/// </summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class TypeColorConverter : IValueConverter
{
    // 타입 카테고리 → 색상 매핑
    private static readonly Dictionary<string, SolidColorBrush> ColorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["string"]   = new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78)),  // 주황
        ["int"]      = new SolidColorBrush(Color.FromRgb(0xB5, 0xCE, 0xA8)),  // 연두
        ["double"]   = new SolidColorBrush(Color.FromRgb(0xB5, 0xCE, 0xA8)),
        ["float"]    = new SolidColorBrush(Color.FromRgb(0xB5, 0xCE, 0xA8)),
        ["bool"]     = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6)),  // 파랑
        ["list"]     = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xB0)),  // 청록
        ["array"]    = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xB0)),
        ["dict"]     = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xAA)),  // 노랑
        ["error"]    = new SolidColorBrush(Color.FromRgb(0xF4, 0x47, 0x47)),  // 빨강
    };

    private static readonly SolidColorBrush DefaultBrush =
        new(Color.FromRgb(0xDC, 0xDC, 0xDC));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string typeName) return DefaultBrush;

        // 타입 이름의 마지막 부분으로 매핑 (예: System.String → String)
        var shortName = typeName.Split('.').LastOrDefault()?.ToLowerInvariant() ?? string.Empty;

        foreach (var (key, brush) in ColorMap)
        {
            if (shortName.Contains(key))
                return brush;
        }

        // 해시 기반 색상 생성 - 같은 타입은 항상 같은 색상
        var hash = Math.Abs(typeName.GetHashCode());
        var hue = (hash % 360) / 360.0;
        return new SolidColorBrush(HsvToRgb(hue, 0.6, 0.8));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Color HsvToRgb(double h, double s, double v)
    {
        var i = (int)(h * 6);
        var f = h * 6 - i;
        var p = v * (1 - s);
        var q = v * (1 - f * s);
        var t = v * (1 - (1 - f) * s);

        var (r, g, b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q)
        };

        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
