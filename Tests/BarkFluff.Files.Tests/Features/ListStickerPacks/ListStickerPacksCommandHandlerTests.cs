using BarkFluff.Files.Features.ListStickerPacks;

namespace BarkFluff.Files.Tests.Features.ListStickerPacks;

public class ListStickerPacksCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private ListStickerPacksCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        _handler = new ListStickerPacksCommandHandler(_helper.StickerPacksStorage);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_ReturnsPaginatedPacks()
    {
        await _helper.SeedStickerPack(name: "Pack A");
        await _helper.SeedStickerPack(name: "Pack B");
        await _helper.SeedStickerPack(name: "Pack C");

        var result = await _handler.Handle(
            new ListStickerPacksCommand { Offset = 0, Limit = 2 }, CancellationToken.None);

        result.Packs.Should().HaveCount(2);
        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_IncludesStickerCounts()
    {
        var pack = await _helper.SeedStickerPack();
        await _helper.SeedSticker(stickerPackId: pack.Id);
        await _helper.SeedSticker(stickerPackId: pack.Id);

        var result = await _handler.Handle(
            new ListStickerPacksCommand { Offset = 0, Limit = 10 }, CancellationToken.None);

        var packInfo = result.Packs.Single(p => p.Id == pack.Id.ToString());
        packInfo.StickerCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_EmptyDatabase_ReturnsEmptyList()
    {
        var result = await _handler.Handle(
            new ListStickerPacksCommand { Offset = 0, Limit = 10 }, CancellationToken.None);

        result.Packs.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }
}
