using System.Globalization;
using System.Windows.Data;

namespace BarkFluff.Builder;

/// <summary>Инвертирует значение переключателя для InfoBar.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool isEnabled && !isEnabled;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool isEnabled && !isEnabled;
}
