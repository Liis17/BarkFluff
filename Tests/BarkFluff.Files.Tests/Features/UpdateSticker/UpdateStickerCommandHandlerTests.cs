using BarkFluff.Files.Features.UpdateSticker;
using BarkFluff.GrpcServer.Settings;

namespace BarkFluff.Files.Tests.Features.UpdateSticker;

public class UpdateStickerCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private UpdateStickerCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://example.com");

        _handler = new UpdateStickerCommandHandler(
            _helper.StickersStorage,
            config.Object,
            new RunSettings { Http1Port = 7005 },
            TestHelper.CreateLogger<UpdateStickerCommandHandler>());

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_UpdatesEmoji()
    {
        var sticker = await _helper.SeedSticker(emoji: "😀");

        var result = await _handler.Handle(
            new UpdateStickerCommand { StickerId = sticker.Id, Emoji = "😎" }, CancellationToken.None);

        result.Sticker.Emoji.Should().Be("😎");
    }

    [Fact]
    public async Task Handle_StickerNotFound_ThrowsException()
    {
        var act = () => _handler.Handle(
            new UpdateStickerCommand { StickerId = Guid.NewGuid(), Emoji = "X" }, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("Стикер не найден");
    }

    [Fact]
    public async Task Handle_PersistsChange()
    {
        var sticker = await _helper.SeedSticker(emoji: "😀");

        await _handler.Handle(
            new UpdateStickerCommand { StickerId = sticker.Id, Emoji = "😎" }, CancellationToken.None);

        var updated = await _helper.StickersStorage.GetByIdAsync(sticker.Id);
        updated!.Emoji.Should().Be("😎");
    }
}
