using BarkFluff.Messages.Infrastructure;

namespace BarkFluff.Messages.Tests.Infrastructure;

public class MessageQueueSenderTests
{
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly MessageQueueSender _sender;

    public MessageQueueSenderTests()
    {
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _sender = new MessageQueueSender(_publishEndpoint.Object);
    }

    [Fact]
    public async Task SendMessage_PublishesNewMessageEvent()
    {
        var message = new Domain.Message
        {
            Id = 1,
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            SentAt = DateTime.UtcNow,
            ReadBy = [1],
            Type = Domain.MessageContentType.Generic,
            Content = new Domain.MessageContent { Text = "hello" }
        };
        var chatId = Guid.NewGuid();
        var members = new List<long> { 1, 2 };

        await _sender.SendMessage(message, chatId, members);

        _publishEndpoint.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.NewMessageEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendEdited_PublishesMessageEditedEvent()
    {
        var message = new Domain.Message
        {
            Id = 1,
            ChatId = Guid.NewGuid(),
            SenderId = 1,
            SentAt = DateTime.UtcNow,
            ReadBy = [1],
            Type = Domain.MessageContentType.Generic,
            Content = new Domain.MessageContent { Text = "edited" }
        };

        await _sender.SendEdited(message, Guid.NewGuid(), [1, 2]);

        _publishEndpoint.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageEditedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendDeleted_PublishesMessageDeletedEvent()
    {
        var chatId = Guid.NewGuid();

        await _sender.SendDeleted(chatId, 1, [1, 2]);

        _publishEndpoint.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendPinned_PublishesMessagePinnedEvent()
    {
        var chatId = Guid.NewGuid();

        await _sender.SendPinned(chatId, 1, 1, DateTime.UtcNow, [1, 2]);

        _publishEndpoint.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessagePinnedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendUnpinned_PublishesMessageUnpinnedEvent()
    {
        await _sender.SendUnpinned(Guid.NewGuid(), 1, [1, 2]);

        _publishEndpoint.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageUnpinnedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAllUnpinned_PublishesAllMessagesUnpinnedEvent()
    {
        await _sender.SendAllUnpinned(Guid.NewGuid(), [1, 2]);

        _publishEndpoint.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.AllMessagesUnpinnedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
