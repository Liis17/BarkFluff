using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace BarkFluff.Client.WinUI.Infrastructure.Converters;

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
