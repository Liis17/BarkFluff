using BarkFluff.Files.Persistence;

namespace BarkFluff.Files.Tests.Persistence;

public class StickersStorageTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private StickersStorage Storage => _helper.StickersStorage;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddAsync_SavesSticker()
    {
        var pack = await _helper.SeedStickerPack();
        var sticker = await _helper.SeedSticker(stickerPackId: pack.Id, emoji: "😀");

        var fetched = await Storage.GetByIdAsync(sticker.Id);
        fetched.Should().NotBeNull();
        fetched!.Emoji.Should().Be("😀");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var result = await Storage.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesEmoji()
    {
        var sticker = await _helper.SeedSticker(emoji: "😀");

        sticker.Emoji = "😎";
        await Storage.UpdateAsync(sticker);

        var fetched = await Storage.GetByIdAsync(sticker.Id);
        fetched!.Emoji.Should().Be("😎");
    }

    [Fact]
    public async Task DeleteAsync_RemovesSticker()
    {
        var sticker = await _helper.SeedSticker();

        await Storage.DeleteAsync(sticker.Id);

        var fetched = await Storage.GetByIdAsync(sticker.Id);
        fetched.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NotFound_DoesNothing()
    {
        await Storage.DeleteAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task GetByIdsAsync_ReturnsMatchingStickers()
    {
        var s1 = await _helper.SeedSticker();
        var s2 = await _helper.SeedSticker();
        await _helper.SeedSticker();

        var result = await Storage.GetByIdsAsync([s1.Id, s2.Id]);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByPackIdAsync_ReturnsOrderedByAddedAt()
    {
        var pack = await _helper.SeedStickerPack();
        var s1 = await _helper.SeedSticker(stickerPackId: pack.Id, emoji: "First");
        await Task.Delay(10);
        var s2 = await _helper.SeedSticker(stickerPackId: pack.Id, emoji: "Second");

        var result = await Storage.GetByPackIdAsync(pack.Id);

        result.Should().HaveCount(2);
        result[0].Emoji.Should().Be("First");
        result[1].Emoji.Should().Be("Second");
    }
}
