using FFMpegCore;

namespace BarkFluff.Files.Services;

/// <summary>
/// Извлекает кадр-обложку и размеры из видео через FFmpeg (FFMpegCore).
/// Бинарь ffmpeg/ffprobe берётся из каталога, заданного через GlobalFFOptions в Program.cs.
/// </summary>
public class VideoThumbnailExtractor
{
    private readonly ILogger<VideoThumbnailExtractor> _logger;

    /// <summary>
    /// Момент кадра-обложки по умолчанию — 5-я секунда.
    /// </summary>
    public static readonly TimeSpan DefaultFramePosition = TimeSpan.FromSeconds(5);

    public VideoThumbnailExtractor(ILogger<VideoThumbnailExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Считывает размеры и длительность видео из файла на диске.
    /// </summary>
    public virtual async Task<(int Width, int Height, TimeSpan Duration)> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var info = await FFProbe.AnalyseAsync(filePath, cancellationToken: cancellationToken);
        var video = info.PrimaryVideoStream;
        return (video?.Width ?? 0, video?.Height ?? 0, info.Duration);
    }

    /// <summary>
    /// Извлекает один кадр в момент <paramref name="at"/> и возвращает его как JPEG-байты.
    /// Если видео короче запрошенного момента — берём середину. Кадр снимается во временный
    /// файл (без System.Drawing — надёжно на Linux), затем считывается и удаляется.
    /// </summary>
    public virtual async Task<byte[]> ExtractFrameJpegAsync(string filePath, TimeSpan at, CancellationToken cancellationToken = default)
    {
        var (_, _, duration) = await ProbeAsync(filePath, cancellationToken);
        var capture = duration > TimeSpan.Zero && at >= duration
            ? TimeSpan.FromTicks(duration.Ticks / 2)
            : at;

        var tempJpg = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");
        try
        {
            var ok = await FFMpeg.SnapshotAsync(filePath, tempJpg, size: null, captureTime: capture);
            if (!ok || !File.Exists(tempJpg))
                throw new InvalidOperationException("FFmpeg не сгенерировал кадр-обложку");

            return await File.ReadAllBytesAsync(tempJpg, cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(tempJpg))
                    File.Delete(tempJpg);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось удалить временный кадр {TempJpg}", tempJpg);
            }
        }
    }
}
