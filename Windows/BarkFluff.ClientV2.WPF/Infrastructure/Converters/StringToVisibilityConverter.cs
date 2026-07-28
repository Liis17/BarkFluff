using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Converters;

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
