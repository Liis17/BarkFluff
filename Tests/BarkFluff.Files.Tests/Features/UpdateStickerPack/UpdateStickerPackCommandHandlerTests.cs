using BarkFluff.Files.Features.UpdateStickerPack;

namespace BarkFluff.Files.Tests.Features.UpdateStickerPack;

public class UpdateStickerPackCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private UpdateStickerPackCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        _handler = new UpdateStickerPackCommandHandler(
            _helper.StickerPacksStorage,
            _helper.StickersStorage,
            TestHelper.CreateLogger<UpdateStickerPackCommandHandler>());

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_UpdatesPackFields()
    {
        var pack = await _helper.SeedStickerPack(name: "Old", description: "Old desc");

        var command = new UpdateStickerPackCommand
        {
            PackId = pack.Id,
            Name = "New",
            Description = "New desc",
            CoverStickerId = null
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Pack.Name.Should().Be("New");
        result.Pack.Description.Should().Be("New desc");
    }

    [Fact]
    public async Task Handle_SetsCoverSticker()
    {
        var pack = await _helper.SeedStickerPack();
        var sticker = await _helper.SeedSticker(stickerPackId: pack.Id);
        var coverId = sticker.Id;

        var command = new UpdateStickerPackCommand
        {
            PackId = pack.Id,
            Name = pack.Name,
            Description = pack.Description,
            CoverStickerId = coverId
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Pack.CoverStickerId.Should().Be(coverId.ToString());
    }

    [Fact]
    public async Task Handle_PackNotFound_ThrowsException()
    {
        var command = new UpdateStickerPackCommand
        {
            PackId = Guid.NewGuid(),
            Name = "X",
            Description = "Y"
        };

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("Стикерпак не найден");
    }
}
