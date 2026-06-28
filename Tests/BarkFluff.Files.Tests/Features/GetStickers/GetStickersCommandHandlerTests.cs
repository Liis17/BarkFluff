using BarkFluff.Files.Features.GetStickers;
using BarkFluff.GrpcServer.Settings;

namespace BarkFluff.Files.Tests.Features.GetStickers;

public class GetStickersCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private GetStickersCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://example.com");

        _handler = new GetStickersCommandHandler(
            _helper.StickersStorage,
            config.Object,
            new RunSettings { Http1Port = 7005 });

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_ReturnsMatchingStickers()
    {
        var s1 = await _helper.SeedSticker(emoji: "😀");
        var s2 = await _helper.SeedSticker(emoji: "😎");
        await _helper.SeedSticker(emoji: "🥳");

        var result = await _handler.Handle(
            new GetStickersCommand { StickerIds = [s1.Id, s2.Id] }, CancellationToken.None);

        result.Stickers.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_EmptyList_ReturnsEmpty()
    {
        var result = await _handler.Handle(
            new GetStickersCommand { StickerIds = [] }, CancellationToken.None);

        result.Stickers.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_StickersIncludeUrls()
    {
        var s = await _helper.SeedSticker();

        var result = await _handler.Handle(
            new GetStickersCommand { StickerIds = [s.Id] }, CancellationToken.None);

        result.Stickers[0].FileUrl.Should().Contain("/download/");
    }
}
