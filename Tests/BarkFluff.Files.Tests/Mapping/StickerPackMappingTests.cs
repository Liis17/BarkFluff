using BarkFluff.Files.Domain;
using BarkFluff.Files.Mapping;

namespace BarkFluff.Files.Tests.Mapping;

public class StickerPackMappingTests
{
    [Fact]
    public void ToGrpc_StickerPack_MapsAllFields()
    {
        var pack = new StickerPack
        {
            Id = Guid.NewGuid(),
            CreatorUserId = 42,
            CoverStickerId = Guid.NewGuid(),
            Name = "Test Pack",
            Description = "Desc",
            CreatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var result = pack.ToGrpc(stickerCount: 5);

        result.Id.Should().Be(pack.Id.ToString());
        result.CreatorUserId.Should().Be(42);
        result.CoverStickerId.Should().Be(pack.CoverStickerId.Value.ToString());
        result.Name.Should().Be("Test Pack");
        result.Description.Should().Be("Desc");
        result.StickerCount.Should().Be(5);
    }

    [Fact]
    public void ToGrpc_StickerPack_NullCoverStickerId_ReturnsEmpty()
    {
        var pack = new StickerPack
        {
            Id = Guid.NewGuid(),
            CoverStickerId = null
        };

        var result = pack.ToGrpc();

        result.CoverStickerId.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpc_Sticker_MapsAllFields()
    {
        var sticker = new Sticker
        {
            Id = Guid.NewGuid(),
            StickerPackId = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            PreviewFileId = Guid.NewGuid(),
            Emoji = "😀",
            AddedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var result = sticker.ToGrpc("https://example.com/web");

        result.Id.Should().Be(sticker.Id.ToString());
        result.StickerPackId.Should().Be(sticker.StickerPackId.ToString());
        result.FileId.Should().Be(sticker.FileId.ToString());
        result.PreviewFileId.Should().Be(sticker.PreviewFileId.Value.ToString());
        result.Emoji.Should().Be("😀");
        result.FileUrl.Should().Contain(sticker.FileId.ToString());
        result.PreviewUrl.Should().Contain(sticker.PreviewFileId.Value.ToString());
    }

    [Fact]
    public void ToGrpc_Sticker_NullPreviewFileId_PreviewUrlEmpty()
    {
        var sticker = new Sticker
        {
            Id = Guid.NewGuid(),
            StickerPackId = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            PreviewFileId = null,
            Emoji = "😀",
            AddedAt = DateTime.UtcNow
        };

        var result = sticker.ToGrpc("https://example.com/web");

        result.PreviewFileId.Should().BeEmpty();
        result.PreviewUrl.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpc_Sticker_NullBaseUrl_UrlsEmpty()
    {
        var sticker = new Sticker
        {
            Id = Guid.NewGuid(),
            StickerPackId = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            Emoji = "😀",
            AddedAt = DateTime.UtcNow
        };

        var result = sticker.ToGrpc();

        result.FileUrl.Should().BeEmpty();
        result.PreviewUrl.Should().BeEmpty();
    }
}
