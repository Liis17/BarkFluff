using BarkFluff.Shared.Queue.Messages;
using BarkFluff.Updates.Features.PushNotifications;
using BarkFluff.Updates.Features.SubscribeMessagesRead;

using MassTransit;

using Microsoft.Extensions.Logging;

using Moq;

namespace BarkFluff.Updates.Tests.PushNotifications;

public class DismissPushPublisherTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public async Task Handle_BurstForSameUserAndChat_PublishesMaximumId(long first, long second)
    {
        var publishEndpoint = new Mock<IPublishEndpoint>();
        var publisher = new DismissPushPublisher(
            publishEndpoint.Object,
            new DismissPushDebouncer(TimeSpan.FromMilliseconds(25)),
            Mock.Of<ILogger<DismissPushPublisher>>());
        var chatId = Guid.NewGuid();

        await Task.WhenAll(
            publisher.Handle(CreateNotification(chatId, first, 42), CancellationToken.None),
            publisher.Handle(CreateNotification(chatId, second, 42), CancellationToken.None));

        publishEndpoint.Verify(
            endpoint => endpoint.Publish(
                It.Is<DismissPushEvent>(@event => @event.ChatId == chatId && @event.UserId == 42 && @event.MessageId == Math.Max(first, second)),
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
