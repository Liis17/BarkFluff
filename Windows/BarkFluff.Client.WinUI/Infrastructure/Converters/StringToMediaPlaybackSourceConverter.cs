using Microsoft.UI.Xaml.Data;

using Windows.Media.Core;

namespace BarkFluff.Client.WinUI.Infrastructure.Converters;

public sealed class StringToMediaPlaybackSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value is string source ? TryCreate(source) : null;

    /// <summary>
    /// <c>MediaPlayerElement.Source</c> принимает <see cref="IMediaPlaybackSource"/>, а не строку —
    /// без обёртки в <see cref="MediaSource"/> x:Bind падает с "The value cannot be converted to
    /// type IMediaPlaybackSource" уже на материализации шаблона вложения.
    /// </summary>
    public static MediaSource? TryCreate(string source) =>
        string.IsNullOrWhiteSpace(source) || !Uri.TryCreate(source, UriKind.Absolute, out var uri)
            ? null
            : MediaSource.CreateFromUri(uri);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
