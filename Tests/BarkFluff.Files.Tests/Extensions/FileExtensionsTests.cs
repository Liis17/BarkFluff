using BarkFluff.Files.Extensions;

namespace BarkFluff.Files.Tests.Extensions;

public class FileExtensionsTests
{
    [Theory]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("image.png", "image/png")]
    [InlineData("animation.gif", "image/gif")]
    [InlineData("image.bmp", "image/bmp")]
    [InlineData("image.webp", "image/webp")]
    [InlineData("icon.svg", "image/svg+xml")]
    public void GetContentType_ImageExtensions_ReturnsCorrectMime(string fileName, string expected)
    {
        fileName.GetContentType().Should().Be(expected);
    }

    [Theory]
    [InlineData("doc.pdf", "application/pdf")]
    [InlineData("doc.doc", "application/msword")]
    [InlineData("doc.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("sheet.xls", "application/vnd.ms-excel")]
    [InlineData("sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("deck.ppt", "application/vnd.ms-powerpoint")]
    [InlineData("deck.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    public void GetContentType_DocumentExtensions_ReturnsCorrectMime(string fileName, string expected)
    {
        fileName.GetContentType().Should().Be(expected);
    }

    [Theory]
    [InlineData("readme.txt", "text/plain")]
    [InlineData("page.html", "text/html")]
    [InlineData("page.htm", "text/html")]
    [InlineData("style.css", "text/css")]
    [InlineData("script.js", "text/javascript")]
    [InlineData("data.json", "application/json")]
    [InlineData("data.xml", "application/xml")]
    public void GetContentType_TextExtensions_ReturnsCorrectMime(string fileName, string expected)
    {
        fileName.GetContentType().Should().Be(expected);
    }

    [Theory]
    [InlineData("song.mp3", "audio/mpeg")]
    [InlineData("audio.wav", "audio/wav")]
    [InlineData("audio.ogg", "audio/ogg")]
    [InlineData("audio.m4a", "audio/mp4")]
    [InlineData("audio.aac", "audio/aac")]
    [InlineData("audio.flac", "audio/flac")]
    [InlineData("audio.opus", "audio/opus")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("clip.avi", "video/x-msvideo")]
    [InlineData("clip.mov", "video/quicktime")]
    public void GetContentType_MediaExtensions_ReturnsCorrectMime(string fileName, string expected)
    {
        fileName.GetContentType().Should().Be(expected);
    }

    [Theory]
    [InlineData("archive.zip", "application/zip")]
    [InlineData("archive.rar", "application/x-rar-compressed")]
    [InlineData("archive.7z", "application/x-7z-compressed")]
    public void GetContentType_ArchiveExtensions_ReturnsCorrectMime(string fileName, string expected)
    {
        fileName.GetContentType().Should().Be(expected);
    }

    [Theory]
    [InlineData("file.xyz")]
    [InlineData("file.bin")]
    [InlineData("file")]
    public void GetContentType_UnknownExtension_ReturnsOctetStream(string fileName)
    {
        fileName.GetContentType().Should().Be("application/octet-stream");
    }

    [Fact]
    public void GetContentType_NullFileName_ReturnsOctetStream()
    {
        ((string)null!).GetContentType().Should().Be("application/octet-stream");
    }

    [Fact]
    public void GetContentType_EmptyFileName_ReturnsOctetStream()
    {
        string.Empty.GetContentType().Should().Be("application/octet-stream");
    }

    [Fact]
    public void GetContentType_UpperCaseExtension_ReturnsCorrectMime()
    {
        "photo.JPG".GetContentType().Should().Be("image/jpeg");
    }
}
