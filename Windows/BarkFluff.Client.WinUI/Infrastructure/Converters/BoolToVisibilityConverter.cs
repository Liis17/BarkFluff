using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace BarkFluff.Client.WinUI.Infrastructure.Converters;

/// <summary>
/// В WinUI нет встроенного <c>BooleanToVisibilityConverter</c>.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}
