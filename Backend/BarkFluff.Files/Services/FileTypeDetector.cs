namespace BarkFluff.Files.Services;

/// <summary>
/// Определяет тип файла по сигнатуре (magic bytes) содержимого.
/// </summary>
public class FileTypeDetector
{
    // Image signatures
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] BmpSignature = [0x42, 0x4D];
    private static readonly byte[] RiffSignature = [0x52, 0x49, 0x46, 0x46]; // RIFF - used by WebP, WAV, AVI
    private static readonly byte[] WebpMarker = [0x57, 0x45, 0x42, 0x50]; // WEBP at offset 8

    // GIF signature
    private static readonly byte[] GifSignature = [0x47, 0x49, 0x46, 0x38]; // GIF8

    // Video signatures
    private static readonly byte[] Mp4Ftyp = [0x66, 0x74, 0x79, 0x70]; // ftyp at offset 4
    private static readonly byte[] WebmSignature = [0x1A, 0x45, 0xDF, 0xA3]; // EBML
    private static readonly byte[] AviMarker = [0x41, 0x56, 0x49, 0x20]; // AVI at offset 8
    private static readonly byte[] QuickTimeAtom = [0x6D, 0x6F, 0x6F, 0x76]; // moov
    private static readonly byte[] QuickTimeFree = [0x66, 0x72, 0x65, 0x65]; // free
    private static readonly byte[] QuickTimeMdat = [0x6D, 0x64, 0x61, 0x74]; // mdat
    private static readonly byte[] QuickTimeFtyp = [0x66, 0x74, 0x79, 0x70]; // ftyp (for MOV/QT)
    private static readonly byte[] QuickTypeQt = [0x71, 0x74, 0x20, 0x20]; // qt at offset 8

    // Audio signatures
    private static readonly byte[] WaveMarker = [0x57, 0x41, 0x56, 0x45]; // WAVE at offset 8
    private static readonly byte[] M4aMarker1 = [0x4D, 0x34, 0x41, 0x20]; // M4A at offset 8
    private static readonly byte[] M4aMarker2 = [0x4D, 0x34, 0x42, 0x20]; // M4B at offset 8
    private static readonly byte[] IsomMarker = [0x69, 0x73, 0x6F, 0x6D]; // isom at offset 8
    private static readonly byte[] Mp4aMarker = [0x6D, 0x70, 0x34, 0x31]; // mp41 at offset 8
    private static readonly byte[] Mp42Marker = [0x6D, 0x70, 0x34, 0x32]; // mp42 at offset 8
    private static readonly byte[] FlacSignature = [0x66, 0x4C, 0x61, 0x43]; // fLaC

    // MP3 signatures (frame sync)
    private static readonly byte[] Mp3FrameSync1 = [0xFF, 0xFB]; // MPEG Audio Layer 3
    private static readonly byte[] Mp3FrameSync2 = [0xFF, 0xFA];
    private static readonly byte[] Mp3FrameSync3 = [0xFF, 0xF3];
    private static readonly byte[] Mp3FrameSync4 = [0xFF, 0xF2];
    private static readonly byte[] Id3Signature = [0x49, 0x44, 0x33]; // ID3

    // OGG signature (for voice messages)
    private static readonly byte[] OggSignature = [0x4F, 0x67, 0x67, 0x53]; // OggS

    /// <summary>
    /// Определяет тип файла по содержимому.
    /// </summary>
    /// <param name="stream">Поток с содержимым файла (позиция должна быть в начале).</param>
    /// <returns>Определённый тип вложения сообщения.</returns>
    public async Task<DetectedFileType> DetectAsync(Stream stream)
    {
        if (stream == null || stream.Length < 4)
        {
            return DetectedFileType.Unknown;
        }

        var originalPosition = stream.Position;
        stream.Position = 0;

        try
        {
            // Читаем достаточно байтов для всех проверок (минимум 12 для WebP/WAV/AVI)
            var buffer = new byte[Math.Min(32, stream.Length)];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));

            if (bytesRead < 4)
            {
                return DetectedFileType.Unknown;
            }

            // Проверяем GIF первым (отдельный тип)
            if (StartsWith(buffer, GifSignature))
            {
                return DetectedFileType.Gif;
            }

            // Проверяем изображения
            if (IsImage(buffer))
            {
                return DetectedFileType.Image;
            }

            // Проверяем видео
            if (IsVideo(buffer, stream))
            {
                return DetectedFileType.Video;
            }

            // Проверяем OGG (голосовые сообщения)
            if (StartsWith(buffer, OggSignature))
            {
                return DetectedFileType.Voice;
            }

            // Проверяем аудио
            if (IsAudio(buffer, stream))
            {
                return DetectedFileType.Audio;
            }

            return DetectedFileType.Unknown;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static bool IsImage(byte[] buffer)
    {
        // JPEG
        if (StartsWith(buffer, JpegSignature))
        {
            return true;
        }

        // PNG
        if (StartsWith(buffer, PngSignature))
        {
            return true;
        }

        // BMP
        if (StartsWith(buffer, BmpSignature))
        {
            return true;
        }

        // WebP: RIFF....WEBP
        if (StartsWith(buffer, RiffSignature) &&
            buffer.Length >= 12 &&
            MatchesAt(buffer, WebpMarker, 8))
        {
            return true;
        }

        return false;
    }

    private static bool IsVideo(byte[] buffer, Stream stream)
    {
        // WebM (EBML header)
        if (StartsWith(buffer, WebmSignature))
        {
            return true;
        }

        // MP4: xxxxftyp (ftyp atom at offset 4)
        if (buffer.Length >= 8 && MatchesAt(buffer, Mp4Ftyp, 4))
        {
            // Проверяем, что это не аудио (M4A, M4B, isom могут быть аудио)
            // Видео контейнеры обычно содержат: mp42, avc1, M4V, etc.
            // Но для простоты считаем всё с ftyp как MP4, если это не M4A/M4B
            if (buffer.Length >= 12)
            {
                var brand = buffer.AsSpan(8, 4);
                // M4A/M4B - это аудио контейнеры
                if (MatchesAt(buffer, M4aMarker1, 8) || MatchesAt(buffer, M4aMarker2, 8))
                {
                    return false;
                }
            }
            return true;
        }

        // AVI: RIFF....AVI
        if (StartsWith(buffer, RiffSignature) &&
            buffer.Length >= 12 &&
            MatchesAt(buffer, AviMarker, 8))
        {
            return true;
        }

        // QuickTime/MOV: проверяем атомы на offset 4
        if (buffer.Length >= 8)
        {
            if (MatchesAt(buffer, QuickTimeAtom, 4) ||
                MatchesAt(buffer, QuickTimeFree, 4) ||
                MatchesAt(buffer, QuickTimeMdat, 4) ||
                MatchesAt(buffer, QuickTimeFtyp, 4))
            {
                // Дополнительно проверяем что это не чистый QT audio
                if (buffer.Length >= 12 && MatchesAt(buffer, QuickTypeQt, 8))
                {
                    return true; // QuickTime movie
                }

                // Для остальных случаев с moov/free/mdat/ftyp на offset 4
                // предполагаем видео если есть эти атомы
                return true;
            }
        }

        return false;
    }

    private static bool IsAudio(byte[] buffer, Stream stream)
    {
        // MP3: Frame sync или ID3 tag
        if (StartsWith(buffer, Mp3FrameSync1) ||
            StartsWith(buffer, Mp3FrameSync2) ||
            StartsWith(buffer, Mp3FrameSync3) ||
            StartsWith(buffer, Mp3FrameSync4) ||
            StartsWith(buffer, Id3Signature))
        {
            return true;
        }

        // WAV: RIFF....WAVE
        if (StartsWith(buffer, RiffSignature) &&
            buffer.Length >= 12 &&
            MatchesAt(buffer, WaveMarker, 8))
        {
            return true;
        }

        // FLAC
        if (StartsWith(buffer, FlacSignature))
        {
            return true;
        }

        // M4A/AAC: ftyp с M4A, M4B, isom, mp41, mp42 брендами
        if (buffer.Length >= 12 && MatchesAt(buffer, Mp4Ftyp, 4))
        {
            if (MatchesAt(buffer, M4aMarker1, 8) ||
                MatchesAt(buffer, M4aMarker2, 8) ||
                MatchesAt(buffer, IsomMarker, 8) ||
                MatchesAt(buffer, Mp4aMarker, 8) ||
                MatchesAt(buffer, Mp42Marker, 8))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StartsWith(byte[] buffer, byte[] signature)
    {
        if (buffer.Length < signature.Length)
        {
            return false;
        }

        for (int i = 0; i < signature.Length; i++)
        {
            if (buffer[i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesAt(byte[] buffer, byte[] signature, int offset)
    {
        if (buffer.Length < offset + signature.Length)
        {
            return false;
        }

        for (int i = 0; i < signature.Length; i++)
        {
            if (buffer[offset + i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Определённый тип файла.
/// </summary>
public enum DetectedFileType
{
    Unknown,
    Image,
    Video,
    Gif,
    Audio,
    Voice,  // OGG audio для голосовых сообщений
    Document
}
