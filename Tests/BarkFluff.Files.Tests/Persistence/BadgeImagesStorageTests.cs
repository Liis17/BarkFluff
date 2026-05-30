using BarkFluff.Files.Domain;
using BarkFluff.Files.Persistence;

namespace BarkFluff.Files.Tests.Persistence;

public class BadgeImagesStorageTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private BadgeImagesStorage Storage => _helper.BadgeImagesStorage;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddAsync_SavesBadgeImage()
    {
        var badge = new Domain.BadgeImage
        {
            Id = Guid.NewGuid(),
            Filename = "badge.png",
            Size = 512,
            CreatedAt = DateTime.UtcNow
        };

        var result = await Storage.AddAsync(badge);

        result.Should().NotBeNull();
        var fetched = await Storage.GetByIdAsync(badge.Id);
        fetched.Should().NotBeNull();
        fetched!.Filename.Should().Be("badge.png");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var result = await Storage.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesFields()
    {
        var badge = await _helper.SeedBadgeImage();

        badge.Etag = "updated-etag";
        badge.UploadedAt = DateTime.UtcNow;
        await Storage.UpdateAsync(badge);

        var updated = await Storage.GetByIdAsync(badge.Id);
        updated!.Etag.Should().Be("updated-etag");
    }
}
