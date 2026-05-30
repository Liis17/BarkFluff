using BarkFluff.Files.Persistence;

namespace BarkFluff.Files.Tests.Persistence;

public class StickerPacksStorageTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private StickerPacksStorage Storage => _helper.StickerPacksStorage;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddAsync_SavesPack()
    {
        var pack = await _helper.SeedStickerPack(name: "Test Pack");

        var fetched = await Storage.GetByIdAsync(pack.Id);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Test Pack");
    }

    [Fact]
    public async Task GetByIdAsync_IncludesStickers()
    {
        var pack = await _helper.SeedStickerPack();
        await _helper.SeedSticker(stickerPackId: pack.Id, emoji: "😀");
        await _helper.SeedSticker(stickerPackId: pack.Id, emoji: "😎");

        var fetched = await Storage.GetByIdAsync(pack.Id);

        fetched!.Stickers.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdWithoutStickersAsync_ReturnsPackWithoutStickers()
    {
        var pack = await _helper.SeedStickerPack();
        await _helper.SeedSticker(stickerPackId: pack.Id);

        var fetched = await Storage.GetByIdWithoutStickersAsync(pack.Id);

        fetched.Should().NotBeNull();
        fetched!.Stickers.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesPack()
    {
        var pack = await _helper.SeedStickerPack(name: "Old Name");

        pack.Name = "New Name";
        await Storage.UpdateAsync(pack);

        var fetched = await Storage.GetByIdAsync(pack.Id);
        fetched!.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task DeleteAsync_RemovesPack()
    {
        var pack = await _helper.SeedStickerPack();

        await Storage.DeleteAsync(pack.Id);

        var fetched = await Storage.GetByIdAsync(pack.Id);
        fetched.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NotFound_DoesNothing()
    {
        await Storage.DeleteAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task ListAsync_ReturnsOrderedByCreatedDesc()
    {
        var pack1 = await _helper.SeedStickerPack(name: "First");
        await Task.Delay(10);
        var pack2 = await _helper.SeedStickerPack(name: "Second");

        var result = await Storage.ListAsync(0, 10);

        result[0].Name.Should().Be("Second");
        result[1].Name.Should().Be("First");
    }

    [Fact]
    public async Task ListAsync_Pagination()
    {
        for (int i = 0; i < 5; i++)
            await _helper.SeedStickerPack(name: $"Pack {i}");

        var result = await Storage.ListAsync(2, 2);
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTotalCountAsync_ReturnsCorrectCount()
    {
        await _helper.SeedStickerPack();
        await _helper.SeedStickerPack();

        var count = await Storage.GetTotalCountAsync();
        count.Should().Be(2);
    }

    [Fact(Skip = "InMemory EF Core does not support GroupBy with ToDictionaryAsync")]
    public void GetStickerCountsAsync_ReturnsCountsByPack() { }
}
