using BarkFluff.Onliner.Consumers;
using BarkFluff.Onliner.Messages;
using BarkFluff.Proto.Onliner;

using MassTransit;

namespace BarkFluff.Onliner.Tests.Consumers;

/// <summary>
/// Ветвление консюмеров по UserUuid (этап 4.2). Отдельно проверяется, что события БЕЗ нового
/// поля переживаются как раньше: инстансы обновляются не одновременно.
/// </summary>
public class RemoteFanOutConsumerTests
{
    private readonly TestHelper _h = new();

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    [Fact]
    public async Task OnlineStatusConsumer_WithUuid_NotifiesUuidSubscribers()
    {
        var uuid = Guid.NewGuid();
        var (stream, received) = TestHelper.CreateCollectingStatusStream();
        _h.SubscriptionsManager.RegisterSubscription(1, [], stream.Object, [uuid]);

        var consumer = new OnlineStatusChangedConsumer(_h.Notifier);

        await consumer.Consume(Context(new OnlineStatusChangedEvent
        {
            UserId = 0,
            UserUuid = uuid,
            Status = (int)DomainStatusTypeId.Online,
            LastSeen = DateTime.UtcNow,
        }));

        received.Should().ContainSingle().Which.UserUuid.Should().Be(uuid.ToString());
    }

    [Fact]
    public async Task OnlineStatusConsumer_WithoutUuid_KeepsLocalPath()
    {
        var (stream, received) = TestHelper.CreateCollectingStatusStream();
        _h.SubscriptionsManager.RegisterSubscription(1, [10], stream.Object);

        var consumer = new OnlineStatusChangedConsumer(_h.Notifier);

        await consumer.Consume(Context(new OnlineStatusChangedEvent
        {
            UserId = 10,
            UserUuid = null,
            Status = (int)DomainStatusTypeId.Online,
            LastSeen = DateTime.UtcNow,
        }));

        var status = received.Should().ContainSingle().Subject;
        status.UserId.Should().Be(10);
        status.UserUuid.Should().BeEmpty();
    }

    [Fact]
    public async Task TypingConsumer_WithUuid_SkipsSenderExclusion()
    {
        var chatId = Guid.NewGuid().ToString();
        var uuid = Guid.NewGuid();
        var (stream, received) = TestHelper.CreateTypingStream();
        _h.TypingSubscriptionsManager.RegisterSubscription(1, [chatId], stream.Object);

        var consumer = new TypingChangedConsumer(_h.TypingNotifier);

        await consumer.Consume(Context(new TypingChangedEvent
        {
            ChatId = chatId,
            UserId = 0,
            UserUuid = uuid,
            Action = (int)TypingAction.Typing,
        }));

        received.Should().ContainSingle().Which.UserUuid.Should().Be(uuid.ToString());
    }

    [Fact]
    public async Task TypingConsumer_WithoutUuid_StillExcludesSender()
    {
        var chatId = Guid.NewGuid().ToString();
        var (senderStream, senderReceived) = TestHelper.CreateTypingStream();
        var (otherStream, otherReceived) = TestHelper.CreateTypingStream();
        _h.TypingSubscriptionsManager.RegisterSubscription(1, [chatId], senderStream.Object);
        _h.TypingSubscriptionsManager.RegisterSubscription(2, [chatId], otherStream.Object);

        var consumer = new TypingChangedConsumer(_h.TypingNotifier);

        await consumer.Consume(Context(new TypingChangedEvent
        {
            ChatId = chatId,
            UserId = 1,
            UserUuid = null,
            Action = (int)TypingAction.Typing,
        }));

        senderReceived.Should().BeEmpty();
        otherReceived.Should().ContainSingle();
    }
}
