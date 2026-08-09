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
/// Подсветка вкладки профиля: <c>RadioButton.IsChecked</c> только читает индекс (OneWay) —
/// запись идёт обратно через <c>Checked</c> в code-behind (см. <c>OnProfileAttachmentTabChecked</c>),
/// а не через ConvertBack: RadioButton в паре снимает отметку сам, и TwoWay-конвертер с
/// UnsetValue на false-переходе ненадёжно взаимодействует с этим внутренним снятием.
/// </summary>
public sealed class IntEqualsToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int intValue && parameter is string text && int.TryParse(text, out var expected) && intValue == expected;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
