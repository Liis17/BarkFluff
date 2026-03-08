using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace BarkFluff.Files.Services;

public class ImageCompressor
{
    public async Task<byte[]> CompressImageAsync(Stream inputStream, int width = 1024)
    {
        using var image = await Image.LoadAsync(inputStream);

        // Пример: уменьшение до 1024px по ширине
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(width, 0)
        }));

        using var outputStream = new MemoryStream();

        // Указываем качество сжатия JPEG (1–100)
        var encoder = new JpegEncoder { Quality = 75 };

        await image.SaveAsync(outputStream, encoder);

        return outputStream.ToArray();
    }
}