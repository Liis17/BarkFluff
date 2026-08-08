using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace BarkFluff.Client.WinUI.Infrastructure.Converters;

/// <summary>Видимость вкладки вложений в профиле: параметр — индекс вкладки, значение — выбранный индекс.</summary>
public sealed class IntEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int intValue && parameter is string text && int.TryParse(text, out var expected) && intValue == expected
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Переключатель вкладок профиля: <c>ToggleButton.IsChecked</c> в обе стороны с индексом вкладки.
/// Обратный переход в false игнорируется — его посылает WinUI при снятии отметки с соседней
/// кнопки той же <c>GroupName</c>, а не пользовательский выбор «снять текущую вкладку».
/// </summary>
public sealed class IntEqualsToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int intValue && parameter is string text && int.TryParse(text, out var expected) && intValue == expected;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is true && parameter is string text && int.TryParse(text, out var expected)
            ? expected
            : DependencyProperty.UnsetValue;
}
