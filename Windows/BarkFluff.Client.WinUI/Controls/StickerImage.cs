using System.Runtime.InteropServices.WindowsRuntime;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BarkFluff.Client.WinUI.Controls;

public sealed class StickerImage : ContentControl
{
    private static readonly HttpClient Http = new() { MaxResponseContentBufferSize = 16 * 1024 * 1024 };
    private readonly Microsoft.UI.Xaml.Controls.Image _image = new() { Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
    private CancellationTokenSource? _load;
    private int _requestedSize;

    internal static StickerFrameCache CreateCache() => new(Http.GetByteArrayAsync, DecodeFrameAsync);

    public static readonly DependencyProperty SourceUrlProperty = DependencyProperty.Register(
        nameof(SourceUrl), typeof(string), typeof(StickerImage),
        new PropertyMetadata("", (sender, _) => ((StickerImage)sender).Reload()));

    public string SourceUrl
    {
        get => (string)GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    public StickerImage()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = false;
        IsHitTestVisible = false;
        Content = _image;
        Loaded += (_, _) => Reload();
        SizeChanged += (_, _) =>
        {
            if (TargetSize != _requestedSize)
                Reload();
        };
        Unloaded += (_, _) =>
        {
            _load?.Cancel();
            _image.Source = null;
            _requestedSize = 0;
        };
    }

    private int TargetSize => (int)Math.Ceiling(Math.Min(
        double.IsNaN(Width) ? ActualWidth : Width, InterfacePreferences.MaxChatStickerSizeDp));

    private async void Reload()
    {
        _load?.Cancel();
        _image.Source = null;
        if (!IsLoaded)
            return;

        var size = _requestedSize = TargetSize;
        if (size <= 0 || !Uri.TryCreate(SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return;

        using var load = new CancellationTokenSource();
        _load = load;
        try
        {
            var cache = App.Services.GetRequiredService<StickerFrameCache>();
            var session = cache.SessionToken;
            var frame = await cache.GetAsync(uri, size, load.Token);
            if (frame is null || load.IsCancellationRequested || cache.SessionToken != session)
                return;

            var bitmap = new WriteableBitmap(frame.Width, frame.Height);
            using var stream = bitmap.PixelBuffer.AsStream();
            stream.Write(frame.Pixels.Span);
            bitmap.Invalidate();
            _image.Source = bitmap;
        }
        catch (Exception)
        {
            // Недоступное или повреждённое изображение оставляет пустое превью.
        }
        finally
        {
            if (ReferenceEquals(_load, load))
                _load = null;
        }
    }

    private static async Task<StickerFrame?> DecodeFrameAsync(byte[] bytes, int size, CancellationToken token)
    {
        using var source = new MemoryStream(bytes);
        var info = await SixLabors.ImageSharp.Image.IdentifyAsync(source, token);
        if ((long)info.Width * info.Height > 4096L * 4096)
            return null;

        source.Position = 0;
        var target = new Size(size, size);
        using var image = await SixLabors.ImageSharp.Image.LoadAsync<Bgra32>(
            new DecoderOptions { MaxFrames = 1, TargetSize = target }, source, token);
        // ImageSharp 3.1 пропускает масштабирование, если совпадает хотя бы одна сторона.
        if (image.Width > size || image.Height > size)
            image.Mutate(context => context.Resize(new ResizeOptions { Size = target, Mode = ResizeMode.Max }));

        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        // WriteableBitmap принимает BGRA с предварительно умноженными цветовыми каналами.
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var alpha = pixels[offset + 3];
            pixels[offset] = (byte)((pixels[offset] * alpha + 127) / 255);
            pixels[offset + 1] = (byte)((pixels[offset + 1] * alpha + 127) / 255);
            pixels[offset + 2] = (byte)((pixels[offset + 2] * alpha + 127) / 255);
        }
        return new StickerFrame(image.Width, image.Height, pixels);
    }
}
