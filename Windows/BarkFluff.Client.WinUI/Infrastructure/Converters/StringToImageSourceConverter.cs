using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BarkFluff.Client.WinUI.Infrastructure.Converters;

public sealed class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value is string source ? TryCreate(source) : null;

    /// <summary>
    /// Загружает изображение по адресу. Возвращает null, если адрес пустой или невалидный;
    /// саму загрузку <see cref="BitmapImage"/> выполняет асинхронно.
    /// </summary>
    public static BitmapImage? TryCreate(string source) =>
        string.IsNullOrWhiteSpace(source) || !Uri.TryCreate(source, UriKind.Absolute, out var uri)
            ? null
            : new BitmapImage(uri);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
