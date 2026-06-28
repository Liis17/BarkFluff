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

        _publishEndpoint.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.NewMessageEvent>(e =>
                e.ChatId == chatId && e.ChatMembers.SequenceEqual(members)),
            It.IsAny<CancellationToken>()), Times.Once);
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

        var chatId = Guid.NewGuid();

        await _sender.SendEdited(message, chatId, [1, 2]);

        _publishEndpoint.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.MessageEditedEvent>(e =>
                e.ChatId == chatId && e.ChatMembers.SequenceEqual(new[] { 1L, 2L })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendDeleted_PublishesMessageDeletedEvent()
    {
        var chatId = Guid.NewGuid();

        await _sender.SendDeleted(chatId, 7, [1, 2]);

        _publishEndpoint.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.MessageDeletedEvent>(e =>
                e.ChatId == chatId && e.MessageId == 7 && e.ChatMembers.SequenceEqual(new[] { 1L, 2L })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendPinned_PublishesMessagePinnedEvent()
    {
        var chatId = Guid.NewGuid();
        var pinnedAt = DateTime.UtcNow;

        await _sender.SendPinned(chatId, 7, 5, pinnedAt, [1, 2]);

        _publishEndpoint.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.MessagePinnedEvent>(e =>
                e.ChatId == chatId && e.MessageId == 7 && e.PinnerUserId == 5 &&
                e.PinnedAt == pinnedAt && e.ChatMembers.SequenceEqual(new[] { 1L, 2L })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendUnpinned_PublishesMessageUnpinnedEvent()
    {
        var chatId = Guid.NewGuid();

        await _sender.SendUnpinned(chatId, 7, [1, 2]);

        _publishEndpoint.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.MessageUnpinnedEvent>(e =>
                e.ChatId == chatId && e.MessageId == 7 && e.ChatMembers.SequenceEqual(new[] { 1L, 2L })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAllUnpinned_PublishesAllMessagesUnpinnedEvent()
    {
        var chatId = Guid.NewGuid();

        await _sender.SendAllUnpinned(chatId, [1, 2]);

        _publishEndpoint.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.AllMessagesUnpinnedEvent>(e =>
                e.ChatId == chatId && e.ChatMembers.SequenceEqual(new[] { 1L, 2L })),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
