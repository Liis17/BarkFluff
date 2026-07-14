using BarkFluff.Bots.Features.SendBotMessage;
using BarkFluff.GrpcServer.Metrics;

using Grpc.Core;

using Moq;

using MessagesProto = BarkFluff.Proto.Messages;

using Xunit;

namespace BarkFluff.Bots.Tests.Features;

public class SendBotMessageCommandHandlerTests
{
    private readonly Mock<MessagesProto.MessagesServerApi.MessagesServerApiClient> _messagesClient = new();
    private readonly SendBotMessageCommandHandler _handler;

    public SendBotMessageCommandHandlerTests()
    {
        _handler = new SendBotMessageCommandHandler(_messagesClient.Object, new MetricsCollector());
    }

    [Fact]
    public async Task Handle_WithChatId_SendsFromBot()
    {
        SetupSend(new MessagesProto.SendMessageResponse { Message = new Proto.Shared.Message { Id = 7 } });

        var result = await _handler.Handle(new SendBotMessageCommand
        {
            BotId = 1,
            ChatId = "chat-guid",
            Text = "hi",
        }, CancellationToken.None);

        Assert.Equal(7, result.Id);
        _messagesClient.Verify(c => c.SendMessageServerAsync(
            It.Is<MessagesProto.SendMessageServerRequest>(r => r.SenderUserId == 1 && r.ChatId == "chat-guid" && r.Message.Text == "hi"),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithUserId_SendsToUser()
    {
        SetupSend(new MessagesProto.SendMessageResponse { Message = new Proto.Shared.Message { Id = 8 } });

        await _handler.Handle(new SendBotMessageCommand
        {
            BotId = 1,
            UserId = 42,
            Text = "hi",
        }, CancellationToken.None);

        _messagesClient.Verify(c => c.SendMessageServerAsync(
            It.Is<MessagesProto.SendMessageServerRequest>(r => r.SenderUserId == 1 && r.UserId == 42),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoTarget_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _handler.Handle(new SendBotMessageCommand { BotId = 1, Text = "hi" }, CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    private void SetupSend(MessagesProto.SendMessageResponse response)
        => _messagesClient
            .Setup(c => c.SendMessageServerAsync(It.IsAny<MessagesProto.SendMessageServerRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<MessagesProto.SendMessageResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
}
