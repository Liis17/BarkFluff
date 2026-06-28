using BarkFluff.Files.Features.CreateStickerPack;

namespace BarkFluff.Files.Tests.Features.CreateStickerPack;

public class CreateStickerPackCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private CreateStickerPackCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        _handler = new CreateStickerPackCommandHandler(
            _helper.StickerPacksStorage,
            TestHelper.CreateLogger<CreateStickerPackCommandHandler>());

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_CreatesPackWithCorrectFields()
    {
        var command = new CreateStickerPackCommand
        {
            CreatorUserId = 42,
            Name = "Cool Stickers",
            Description = "Best stickers ever"
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Pack.Should().NotBeNull();
        result.Pack.Name.Should().Be("Cool Stickers");
        result.Pack.Description.Should().Be("Best stickers ever");
        result.Pack.CreatorUserId.Should().Be(42);
        result.Pack.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_PersistsToDatabase()
    {
        var command = new CreateStickerPackCommand
        {
            CreatorUserId = 1,
            Name = "Test",
            Description = "Desc"
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        var packId = Guid.Parse(result.Pack.Id);
        var fetched = await _helper.StickerPacksStorage.GetByIdAsync(packId);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Test");
    }
}
