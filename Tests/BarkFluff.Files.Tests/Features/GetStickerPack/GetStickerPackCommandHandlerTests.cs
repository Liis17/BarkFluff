using BarkFluff.Files.Features.GetStickerPack;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Shared.Exceptions.Files;

namespace BarkFluff.Files.Tests.Features.GetStickerPack;

public class GetStickerPackCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private GetStickerPackCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://example.com");

        _handler = new GetStickerPackCommandHandler(
            _helper.StickerPacksStorage,
            _helper.UserStickerPacksStorage,
            config.Object,
            new RunSettings { Http1Port = 7005 });

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_ReturnsPackWithStickers()
    {
        var pack = await _helper.SeedStickerPack();
        await _helper.SeedSticker(stickerPackId: pack.Id, emoji: "😀");
        await _helper.SeedSticker(stickerPackId: pack.Id, emoji: "😎");

        var result = await _handler.Handle(
            new GetStickerPackCommand { PackId = pack.Id }, CancellationToken.None);

        result.Pack.Should().NotBeNull();
        result.Pack.Id.Should().Be(pack.Id.ToString());
        result.Stickers.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_PackNotFound_ThrowsException()
    {
        var act = () => _handler.Handle(
            new GetStickerPackCommand { PackId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<StickerPackNotFoundException>();
    }

    [Fact]
    public async Task Handle_PackAddedByUser_IsAddedTrue()
    {
        var pack = await _helper.SeedStickerPack();
        await _helper.SeedUserStickerPack(userId: 7, pack.Id);

        var added = await _handler.Handle(new GetStickerPackCommand { PackId = pack.Id, UserId = 7 }, CancellationToken.None);
        var other = await _handler.Handle(new GetStickerPackCommand { PackId = pack.Id, UserId = 8 }, CancellationToken.None);

        added.IsAdded.Should().BeTrue();
        other.IsAdded.Should().BeFalse();
    }
}
