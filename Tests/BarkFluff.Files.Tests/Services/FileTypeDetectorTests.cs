using BarkFluff.Files.Services;

namespace BarkFluff.Files.Tests.Services;

public class FileTypeDetectorTests
{
    private readonly FileTypeDetector _detector = new();

    private static Stream BuildStream(params byte[] bytes) => new MemoryStream(bytes);

    private static Stream BuildStreamWithPadding(byte[] prefix, int totalLength)
    {
        var buffer = new byte[totalLength];
        Array.Copy(prefix, buffer, prefix.Length);
        return new MemoryStream(buffer);
    }

    [Fact]
    public async Task DetectAsync_NullStream_ReturnsUnknown()
    {
        var result = await _detector.DetectAsync(null!);
        result.Should().Be(DetectedFileType.Unknown);
    }

    [Fact]
    public async Task DetectAsync_EmptyStream_ReturnsUnknown()
    {
        var result = await _detector.DetectAsync(new MemoryStream());
        result.Should().Be(DetectedFileType.Unknown);
    }

    [Fact]
    public async Task DetectAsync_TooSmallStream_ReturnsUnknown()
    {
        var result = await _detector.DetectAsync(new MemoryStream([0xFF]));
        result.Should().Be(DetectedFileType.Unknown);
    }

    [Fact]
    public async Task DetectAsync_Jpeg_ReturnsImage()
    {
        var stream = BuildStream(0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Image);
    }

    [Fact]
    public async Task DetectAsync_Png_ReturnsImage()
    {
        var stream = BuildStream(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Image);
    }

    [Fact]
    public async Task DetectAsync_Bmp_ReturnsImage()
    {
        var stream = BuildStream(0x42, 0x4D, 0x00, 0x00, 0x00, 0x00);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Image);
    }

    [Fact]
    public async Task DetectAsync_TiffLittleEndian_ReturnsImage()
    {
        var stream = BuildStream(0x49, 0x49, 0x2A, 0x00, 0x00, 0x00);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Image);
    }

    [Fact]
    public async Task DetectAsync_TiffBigEndian_ReturnsImage()
    {
        var stream = BuildStream(0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Image);
    }

    [Fact]
    public async Task DetectAsync_Gif_ReturnsGif()
    {
        var stream = BuildStream(0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x00, 0x00);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Gif);
    }

    [Fact]
    public async Task DetectAsync_WebP_ReturnsSticker()
    {
        var buffer = new byte[16];
        buffer[0] = 0x52; buffer[1] = 0x49; buffer[2] = 0x46; buffer[3] = 0x46;
        buffer[8] = 0x57; buffer[9] = 0x45; buffer[10] = 0x42; buffer[11] = 0x50;
        var stream = new MemoryStream(buffer);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Sticker);
    }

    [Fact]
    public async Task DetectAsync_Mp4Video_ReturnsVideo()
    {
        var buffer = new byte[16];
        buffer[4] = 0x66; buffer[5] = 0x74; buffer[6] = 0x79; buffer[7] = 0x70;
        buffer[8] = 0x69; buffer[9] = 0x73; buffer[10] = 0x6F; buffer[11] = 0x6D;
        var stream = new MemoryStream(buffer);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Video);
    }

    [Fact]
    public async Task DetectAsync_WebM_ReturnsVideo()
    {
        var stream = BuildStream(0x1A, 0x45, 0xDF, 0xA3, 0x00, 0x00);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Video);
    }

    [Fact]
    public async Task DetectAsync_Avi_ReturnsVideo()
    {
        var buffer = new byte[16];
        buffer[0] = 0x52; buffer[1] = 0x49; buffer[2] = 0x46; buffer[3] = 0x46;
        buffer[8] = 0x41; buffer[9] = 0x56; buffer[10] = 0x49; buffer[11] = 0x20;
        var stream = new MemoryStream(buffer);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Video);
    }

    [Fact]
    public async Task DetectAsync_Mp4WithM4aMarker_ReturnsAudioNotVideo()
    {
        var buffer = new byte[16];
        buffer[4] = 0x66; buffer[5] = 0x74; buffer[6] = 0x79; buffer[7] = 0x70;
        buffer[8] = 0x4D; buffer[9] = 0x34; buffer[10] = 0x41; buffer[11] = 0x20;
        var stream = new MemoryStream(buffer);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Audio);
    }

    [Fact]
    public async Task DetectAsync_Mp4WithM4bMarker_ReturnsAudioNotVideo()
    {
        var buffer = new byte[16];
        buffer[4] = 0x66; buffer[5] = 0x74; buffer[6] = 0x79; buffer[7] = 0x70;
        buffer[8] = 0x4D; buffer[9] = 0x34; buffer[10] = 0x42; buffer[11] = 0x20;
        var stream = new MemoryStream(buffer);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Audio);
    }

    [Fact]
    public async Task DetectAsync_Ogg_ReturnsVoice()
    {
        var stream = BuildStream(0x4F, 0x67, 0x67, 0x53, 0x00, 0x00);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Voice);
    }

    [Fact]
    public async Task DetectAsync_Mp3WithFrameSync_ReturnsAudio()
    {
        var stream = BuildStream(0xFF, 0xFB, 0x00, 0x00, 0x00, 0x00);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Audio);
    }

    [Fact]
    public async Task DetectAsync_Mp3WithID3Tag_ReturnsAudio()
    {
        var stream = BuildStream(0x49, 0x44, 0x33, 0x00, 0x00, 0x00);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Audio);
    }

    [Fact]
    public async Task DetectAsync_Wav_ReturnsAudio()
    {
        var buffer = new byte[16];
        buffer[0] = 0x52; buffer[1] = 0x49; buffer[2] = 0x46; buffer[3] = 0x46;
        buffer[8] = 0x57; buffer[9] = 0x41; buffer[10] = 0x56; buffer[11] = 0x45;
        var stream = new MemoryStream(buffer);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Audio);
    }

    [Fact]
    public async Task DetectAsync_Flac_ReturnsAudio()
    {
        var stream = BuildStream(0x66, 0x4C, 0x61, 0x43, 0x00, 0x00);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Audio);
    }

    [Fact]
    public async Task DetectAsync_M4aWithIsomBrand_ReturnsVideo()
    {
        var buffer = new byte[16];
        buffer[4] = 0x66; buffer[5] = 0x74; buffer[6] = 0x79; buffer[7] = 0x70;
        buffer[8] = 0x69; buffer[9] = 0x73; buffer[10] = 0x6F; buffer[11] = 0x6D;
        var stream = new MemoryStream(buffer);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Video);
    }

    [Fact]
    public async Task DetectAsync_Heic_ReturnsImage()
    {
        var buffer = new byte[16];
        buffer[4] = 0x66; buffer[5] = 0x74; buffer[6] = 0x79; buffer[7] = 0x70;
        buffer[8] = 0x68; buffer[9] = 0x65; buffer[10] = 0x69; buffer[11] = 0x63;
        var stream = new MemoryStream(buffer);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Image);
    }

    [Fact]
    public async Task DetectAsync_Avif_ReturnsImage()
    {
        var buffer = new byte[16];
        buffer[4] = 0x66; buffer[5] = 0x74; buffer[6] = 0x79; buffer[7] = 0x70;
        buffer[8] = 0x61; buffer[9] = 0x76; buffer[10] = 0x69; buffer[11] = 0x66;
        var stream = new MemoryStream(buffer);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Image);
    }

    [Fact]
    public async Task DetectAsync_UnknownContent_ReturnsUnknown()
    {
        var stream = BuildStream(0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x00);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Unknown);
    }

    [Fact]
    public async Task DetectAsync_PreservesStreamPosition()
    {
        var stream = BuildStream(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        stream.Position = 3;
        var result = await _detector.DetectAsync(stream);
        stream.Position.Should().Be(3);
        result.Should().Be(DetectedFileType.Image);
    }

    [Fact]
    public async Task DetectAsync_Mp4WithMp41Brand_ReturnsVideo()
    {
        var buffer = new byte[16];
        buffer[4] = 0x66; buffer[5] = 0x74; buffer[6] = 0x79; buffer[7] = 0x70;
        buffer[8] = 0x6D; buffer[9] = 0x70; buffer[10] = 0x34; buffer[11] = 0x31;
        var stream = new MemoryStream(buffer);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Video);
    }

    [Fact]
    public async Task DetectAsync_Mp4WithMp42Brand_ReturnsVideo()
    {
        var buffer = new byte[16];
        buffer[4] = 0x66; buffer[5] = 0x74; buffer[6] = 0x79; buffer[7] = 0x70;
        buffer[8] = 0x6D; buffer[9] = 0x70; buffer[10] = 0x34; buffer[11] = 0x32;
        var stream = new MemoryStream(buffer);
        var result = await _detector.DetectAsync(stream);
        result.Should().Be(DetectedFileType.Video);
    }
}
