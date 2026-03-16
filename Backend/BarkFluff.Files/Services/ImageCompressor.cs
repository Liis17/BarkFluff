using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace BarkFluff.Files.Services;

public class ImageCompressor
{
    /// <summary>
    /// Максимальная сторона изображения (по дизайн-документу).
    /// </summary>
    private const int MaxOriginalSide = 2500;

    /// <summary>
    /// Максимальный размер изображения в байтах (2 МБ).
    /// </summary>
    private const long MaxOriginalSizeBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Качество JPEG для принудительного сжатия оригинала (по дизайн-документу).
    /// </summary>
    private const int OriginalJpegQuality = 90;

    /// <summary>
    /// Качество JPEG для превью (thumbnail).
    /// </summary>
    private const int PreviewJpegQuality = 75;

    /// <summary>
    /// Генерация превью (thumbnail) для быстрой загрузки.
    /// </summary>
    public async Task<byte[]> CompressImageAsync(Stream inputStream, int width = 1024)
    {
        using var image = await Image.LoadAsync(inputStream);

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(width, 0)
        }));

        using var outputStream = new MemoryStream();
        var encoder = new JpegEncoder { Quality = PreviewJpegQuality };
        await image.SaveAsync(outputStream, encoder);

        return outputStream.ToArray();
    }

    /// <summary>
    /// Принудительное сжатие оригинала по требованиям дизайн-документа:
    /// - Макс. сторона: 2500 px
    /// - Макс. размер: 2 МБ
    /// - Формат: JPEG 90%
    /// Возвращает (сжатые байты, true) если сжатие было применено, иначе (null, false).
    /// </summary>
    public async Task<(byte[]? CompressedBytes, bool WasCompressed)> EnforceOriginalLimitsAsync(Stream inputStream)
    {
        var streamLength = inputStream.Length;

        using var image = await Image.LoadAsync(inputStream);

        var needsResize = image.Width > MaxOriginalSide || image.Height > MaxOriginalSide;
        var needsCompress = streamLength > MaxOriginalSizeBytes;

        if (!needsResize && !needsCompress)
            return (null, false);

        if (needsResize)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxOriginalSide, MaxOriginalSide)
            }));
        }

        using var outputStream = new MemoryStream();
        var encoder = new JpegEncoder { Quality = OriginalJpegQuality };
        await image.SaveAsync(outputStream, encoder);

        return (outputStream.ToArray(), true);
    }

    /// <summary>
    /// Генерация превью стикера (WebP, 64x64).
    /// </summary>
    public async Task<byte[]> GenerateStickerPreviewAsync(Stream inputStream, int size = 64)
    {
        using var image = await Image.LoadAsync(inputStream);

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(size, size)
        }));

        using var outputStream = new MemoryStream();
        await image.SaveAsync(outputStream, new WebpEncoder());

        return outputStream.ToArray();
    }
}