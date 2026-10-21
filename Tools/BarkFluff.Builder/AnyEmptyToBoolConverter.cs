using System.Globalization;
using System.Windows.Data;

namespace BarkFluff.Builder;

/// <summary>Показывает предупреждение, если хотя бы один путь к сертификату пуст.</summary>
public sealed class AnyEmptyToBoolConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Any(value => string.IsNullOrWhiteSpace(value as string));

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
