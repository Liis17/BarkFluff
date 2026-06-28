using BarkFluff.Files.Features.RemoveSticker;

namespace BarkFluff.Files.Tests.Features.RemoveSticker;

public class RemoveStickerCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private RemoveStickerCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        _handler = new RemoveStickerCommandHandler(
            _helper.StickersStorage,
            _helper.StickerPacksStorage,
            TestHelper.CreateLogger<RemoveStickerCommandHandler>());

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_RemovesSticker()
    {
        var sticker = await _helper.SeedSticker();

        await _handler.Handle(
            new RemoveStickerCommand { StickerId = sticker.Id }, CancellationToken.None);

        var deleted = await _helper.StickersStorage.GetByIdAsync(sticker.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task Handle_StickerNotFound_DoesNotDeleteOtherStickers()
    {
        var existing = await _helper.SeedSticker();

        await _handler.Handle(
            new RemoveStickerCommand { StickerId = Guid.NewGuid() }, CancellationToken.None);

        var survived = await _helper.StickersStorage.GetByIdAsync(existing.Id);
        survived.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_CoverStickerRemoved_ResetsPackCover()
    {
        var pack = await _helper.SeedStickerPack();
        var sticker = await _helper.SeedSticker(stickerPackId: pack.Id);
        pack.CoverStickerId = sticker.Id;
        await _helper.StickerPacksStorage.UpdateAsync(pack);

        await _handler.Handle(
            new RemoveStickerCommand { StickerId = sticker.Id }, CancellationToken.None);

        var updatedPack = await _helper.StickerPacksStorage.GetByIdAsync(pack.Id);
        updatedPack!.CoverStickerId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NonCoverStickerRemoved_DoesNotChangeCover()
    {
        var pack = await _helper.SeedStickerPack();
        var coverSticker = await _helper.SeedSticker(stickerPackId: pack.Id);
        var otherSticker = await _helper.SeedSticker(stickerPackId: pack.Id);
        pack.CoverStickerId = coverSticker.Id;
        await _helper.StickerPacksStorage.UpdateAsync(pack);

        await _handler.Handle(
            new RemoveStickerCommand { StickerId = otherSticker.Id }, CancellationToken.None);

        var updatedPack = await _helper.StickerPacksStorage.GetByIdAsync(pack.Id);
        updatedPack!.CoverStickerId.Should().Be(coverSticker.Id);
    }
}
