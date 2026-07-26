using BarkFluff.Bots.Features.DeleteBotMessage;
using BarkFluff.Bots.Features.EditBotMessage;
using BarkFluff.GrpcServer.Metrics;

using Grpc.Core;

using Moq;

using MessagesProto = BarkFluff.Proto.Messages;

using Xunit;

namespace BarkFluff.Bots.Tests.Features;

public class EditBotMessageCommandHandlerTests
{
    private readonly Mock<MessagesProto.MessagesServerApi.MessagesServerApiClient> _messagesClient = new();
    private readonly EditBotMessageCommandHandler _editHandler;
    private readonly DeleteBotMessageCommandHandler _deleteHandler;

    public EditBotMessageCommandHandlerTests()
    {
        _editHandler = new EditBotMessageCommandHandler(_messagesClient.Object, new MetricsCollector());
        _deleteHandler = new DeleteBotMessageCommandHandler(_messagesClient.Object, new MetricsCollector());
    }

    [Fact]
    public async Task Edit_SendsBotAsAuthor()
    {
        _messagesClient
            .Setup(c => c.EditMessageServerAsync(
                It.IsAny<MessagesProto.EditMessageServerRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Call(new MessagesProto.EditMessageResponse
            {
                Message = new Proto.Shared.Message { Id = 5, Content = new Proto.Shared.MessageContent { Text = "новый" } },
            }));

        var result = await _editHandler.Handle(new EditBotMessageCommand
        {
            BotId = 1,
            MessageId = 5,
            Text = "новый",
            FileIds = ["file-a"],
        }, CancellationToken.None);

        Assert.Equal("новый", result.Content.Text);
        _messagesClient.Verify(c => c.EditMessageServerAsync(
            It.Is<MessagesProto.EditMessageServerRequest>(r =>
                r.SenderUserId == 1 && r.MessageId == 5 && r.Text == "новый" && r.FilesIds.Contains("file-a")),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_SendsBotAsAuthor()
    {
        _messagesClient
            .Setup(c => c.DeleteMessageServerAsync(
                It.IsAny<MessagesProto.DeleteMessageServerRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Call(new MessagesProto.DeleteMessageResponse()));

        await _deleteHandler.Handle(new DeleteBotMessageCommand { BotId = 1, MessageId = 9 }, CancellationToken.None);

        _messagesClient.Verify(c => c.DeleteMessageServerAsync(
            It.Is<MessagesProto.DeleteMessageServerRequest>(r => r.SenderUserId == 1 && r.MessageId == 9),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Edit_InvalidMessageId_ThrowsInvalidArgument(long messageId)
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _editHandler.Handle(
                new EditBotMessageCommand { BotId = 1, MessageId = messageId, Text = "x" }, CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Delete_InvalidMessageId_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _deleteHandler.Handle(
                new DeleteBotMessageCommand { BotId = 1, MessageId = 0 }, CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    private static AsyncUnaryCall<T> Call<T>(T response)
        => new(Task.FromResult(response),
            Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
}
