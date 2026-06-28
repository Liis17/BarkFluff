using BarkFluff.Files.Features.DeleteStickerPack;

namespace BarkFluff.Files.Tests.Features.DeleteStickerPack;

public class DeleteStickerPackCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private DeleteStickerPackCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        _handler = new DeleteStickerPackCommandHandler(
            _helper.StickerPacksStorage,
            TestHelper.CreateLogger<DeleteStickerPackCommandHandler>());

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_DeletesPack()
    {
        var pack = await _helper.SeedStickerPack();

        await _handler.Handle(new DeleteStickerPackCommand { PackId = pack.Id }, CancellationToken.None);

        var deleted = await _helper.StickerPacksStorage.GetByIdAsync(pack.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NonExistentPack_DoesNotDeleteOtherPacks()
    {
        var existing = await _helper.SeedStickerPack();

        await _handler.Handle(
            new DeleteStickerPackCommand { PackId = Guid.NewGuid() }, CancellationToken.None);

        var survived = await _helper.StickerPacksStorage.GetByIdAsync(existing.Id);
        survived.Should().NotBeNull();
    }
}
