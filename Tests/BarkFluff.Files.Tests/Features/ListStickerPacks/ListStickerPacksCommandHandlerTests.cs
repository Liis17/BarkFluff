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

    [Fact(Skip = "InMemory EF Core does not support GroupBy with ToDictionaryAsync")]
    public void Handle_ReturnsPaginatedPacks() { }

    [Fact(Skip = "InMemory EF Core does not support GroupBy with ToDictionaryAsync")]
    public void Handle_IncludesStickerCounts() { }

    [Fact(Skip = "InMemory EF Core does not support GroupBy with ToDictionaryAsync")]
    public void Handle_EmptyDatabase_ReturnsEmptyList() { }
}
