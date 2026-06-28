using BarkFluff.Files.Domain;
using BarkFluff.Files.Mapping;

namespace BarkFluff.Files.Tests.Mapping;

public class UploadFileMappingTests
{
    [Fact]
    public void ToGrpc_MapsAllFields()
    {
        var file = new UploadFile
        {
            Id = Guid.NewGuid(),
            Uploaders = [1, 2],
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UploadedAt = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Etag = "test-etag",
            Type = UploadFileType.MessageAttachmentImage,
            Filename = "photo.jpg",
            PreviewId = Guid.NewGuid(),
            Size = 1024,
            ImageWidth = 800,
            ImageHeight = 600
        };

        var result = file.ToGrpc("https://example.com/web");

        result.Id.Should().Be(file.Id.ToString());
        result.Uploaders.Should().Equal(1L, 2L);
        result.Etag.Should().Be("test-etag");
        result.Type.Should().Be(Proto.Files.UploadFileType.MessageAttachmentImage);
        result.FileName.Should().Be("photo.jpg");
        result.FileSize.Should().Be(1024);
        result.PreviewFileId.Should().Be(file.PreviewId.Value.ToString());
        result.ImageWidth.Should().Be(800);
        result.ImageHeight.Should().Be(600);
        result.FileUrl.Should().Contain("/download/");
        result.FileUrl.Should().Contain(file.Id.ToString());
        result.PreviewUrl.Should().Contain(file.PreviewId.Value.ToString());
    }

    [Fact]
    public void ToGrpc_NullEtag_ReturnsEmptyString()
    {
        var file = new UploadFile { Id = Guid.NewGuid(), Etag = null };

        var result = file.ToGrpc();

        result.Etag.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpc_NullUploadedAt_ReturnsMinTimestamp()
    {
        var file = new UploadFile { Id = Guid.NewGuid(), UploadedAt = null };

        var result = file.ToGrpc();

        result.UploadedAt.Should().NotBeNull();
    }

    [Fact]
    public void ToGrpc_NullPreviewId_PreviewFileIdIsEmpty()
    {
        var file = new UploadFile { Id = Guid.NewGuid(), PreviewId = null };

        var result = file.ToGrpc();

        result.PreviewFileId.Should().BeEmpty();
        result.PreviewUrl.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpc_NoBaseUrl_FileUrlIsEmpty()
    {
        var file = new UploadFile { Id = Guid.NewGuid() };

        var result = file.ToGrpc();

        result.FileUrl.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpc_NullImageDimensions_ReturnsZero()
    {
        var file = new UploadFile { Id = Guid.NewGuid(), ImageWidth = null, ImageHeight = null };

        var result = file.ToGrpc();

        result.ImageWidth.Should().Be(0);
        result.ImageHeight.Should().Be(0);
    }
}
