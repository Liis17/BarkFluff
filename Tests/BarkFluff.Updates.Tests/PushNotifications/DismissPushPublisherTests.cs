using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.PushNotifications;
using BarkFluff.Updates.Features.SubscribeMessagesRead;

using MassTransit;

using Microsoft.Extensions.Logging;

using Moq;

namespace BarkFluff.Updates.Tests.PushNotifications;

public class DismissPushPublisherTests
{
    [Fact]
    public async Task Handle_BurstForSameUserAndChat_PublishesOnce()
    {
        var publishEndpoint = new Mock<IPublishEndpoint>();
        var publisher = new DismissPushPublisher(
            publishEndpoint.Object,
            new DismissPushDebouncer(TimeSpan.FromMilliseconds(25)),
            Mock.Of<ILogger<DismissPushPublisher>>());
        var chatId = Guid.NewGuid();

        await Task.WhenAll(
            publisher.Handle(CreateNotification(chatId, 1, 42), CancellationToken.None),
            publisher.Handle(CreateNotification(chatId, 2, 42), CancellationToken.None));

        publishEndpoint.Verify(
            endpoint => endpoint.Publish(
                It.Is<DismissPushEvent>(@event => @event.ChatId == chatId && @event.UserId == 42),
                It.IsAny<CancellationToken>()),
            Times.Once);
        publishEndpoint.Verify(
            endpoint => endpoint.Publish(
                It.IsAny<DismissPushEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ReadByNotification CreateNotification(Guid chatId, long messageId, long userId)
    {
        return new ReadByNotification
        {
            ChatId = chatId,
            MessageId = messageId,
            NewReadBy = [10, userId],
            NewReaders = [userId],
            ChatMembers = [userId]
        };
    }
}
