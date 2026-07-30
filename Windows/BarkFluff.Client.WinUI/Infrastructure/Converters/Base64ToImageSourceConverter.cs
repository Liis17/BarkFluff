using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

using System.Runtime.InteropServices.WindowsRuntime;

using Windows.Storage.Streams;

namespace BarkFluff.Client.WinUI.Infrastructure.Converters;

/// <summary>
/// Разворачивает base64-PNG (QR-код FastAuth) в картинку, чтобы ViewModel хранила строку,
/// а не тип представления.
/// </summary>
public sealed class Base64ToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string { Length: > 0 } base64)
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = System.Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        var stream = new InMemoryRandomAccessStream();
        stream.WriteAsync(bytes.AsBuffer()).AsTask().GetAwaiter().GetResult();
        stream.Seek(0);

        var image = new BitmapImage();
        image.SetSource(stream);
        return image;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
