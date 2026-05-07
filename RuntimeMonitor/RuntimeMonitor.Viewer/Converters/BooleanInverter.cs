using System.Globalization;
using System.Windows.Data;

namespace RuntimeMonitor.Viewer.Converters;

/// <summary>bool 값을 반전시키는 컨버터 (IsEnabled 바인딩 등에서 사용)</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class BooleanInverter : IValueConverter
{
    public static readonly BooleanInverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
