using BarkFluff.Files.Features.GetStickerPack;
using BarkFluff.Files.Features.GetStickerPackByFile;

using MediatR;

using Moq;

namespace BarkFluff.Files.Tests.Features.GetStickerPackByFile;

public class GetStickerPackByFileCommandHandlerTests : IAsyncLifetime
{
    private readonly TestHelper _helper = new();
    private readonly Mock<IMediator> _mediator = new();
    private GetStickerPackByFileCommandHandler _handler = null!;

    public Task InitializeAsync()
    {
        _handler = new GetStickerPackByFileCommandHandler(
            _helper.StickersStorage,
            _mediator.Object);

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Handle_ResolvesPackIdByStickerFileAndDelegates()
    {
        var pack = await _helper.SeedStickerPack();
        var fileId = Guid.NewGuid();
        await _helper.SeedSticker(stickerPackId: pack.Id, fileId: fileId, emoji: "😀");

        var expected = new BarkFluff.Proto.Files.GetStickerPackResponse();
        _mediator
            .Setup(m => m.Send(It.Is<GetStickerPackCommand>(c => c.PackId == pack.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _handler.Handle(
            new GetStickerPackByFileCommand { FileId = fileId }, CancellationToken.None);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Handle_StickerNotFound_ThrowsException()
    {
        var act = () => _handler.Handle(
            new GetStickerPackByFileCommand { FileId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("Стикер не найден");
    }
}
