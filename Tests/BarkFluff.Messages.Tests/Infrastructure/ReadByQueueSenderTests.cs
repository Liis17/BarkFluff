using BarkFluff.Messages.Infrastructure;

namespace BarkFluff.Messages.Tests.Infrastructure;

public class ReadByQueueSenderTests
{
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly ReadByQueueSender _sender;

    public ReadByQueueSenderTests()
    {
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _sender = new ReadByQueueSender(_publishEndpoint.Object);
    }

    [Fact]
    public async Task SendEvent_PublishesMessageReadEvent()
    {
        var chatId = Guid.NewGuid();
        var readBy = new List<long> { 1, 2 };
        var chatMembers = new List<long> { 1, 2, 3 };

        await _sender.SendEvent(chatId, 42, readBy, chatMembers);

        _publishEndpoint.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.MessageReadEvent>(e =>
                e.ChatId == chatId && e.MessageId == 42 &&
                e.NewReadBy.SequenceEqual(readBy) && e.ChatMembers.SequenceEqual(chatMembers)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
