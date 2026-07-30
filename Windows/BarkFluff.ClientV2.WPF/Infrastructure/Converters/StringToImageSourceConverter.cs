using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Converters;

public sealed class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string source ? TryCreate(source) : null;

    /// <summary>
    /// Загружает изображение по адресу. Возвращает null, если адрес пустой, невалидный
    /// или изображение не удалось загрузить.
    /// </summary>
    public static BitmapImage? TryCreate(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || !Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = uri;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();

        if (image.CanFreeze)
        {
            image.Freeze();
        }

        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
