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

        _publishEndpoint.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.MessageReadEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
