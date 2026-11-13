using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Updates;
using BarkFluff.Updates.Features.SubscribeChatFoldersChanged;
using BarkFluff.Updates.Features.SubscribeChatFoldersChanged.Handlers;

using Grpc.Core;

using Microsoft.Extensions.Logging.Abstractions;

namespace BarkFluff.Updates.Tests.Features.SubscribeChatFoldersChanged;

public class ChatFoldersChangedNotificationHandlerTests
{
    private readonly StreamSubscriptionsManager _subscriptions = new();

    private ChatFoldersChangedNotificationHandler Handler() =>
        new(_subscriptions, NullLogger<ChatFoldersChangedNotificationHandler>.Instance, new MetricsCollector());

    [Fact]
    public async Task Handle_SendsSignalToEveryStreamOfUserOnly()
    {
        var phone = new Mock<IServerStreamWriter<ChatFoldersChangedEvent>>();
        var desktop = new Mock<IServerStreamWriter<ChatFoldersChangedEvent>>();
        var otherUser = new Mock<IServerStreamWriter<ChatFoldersChangedEvent>>();
        _subscriptions.RegisterSubscription(1, phone.Object);
        _subscriptions.RegisterSubscription(1, desktop.Object);
        _subscriptions.RegisterSubscription(2, otherUser.Object);

        await Handler().Handle(new ChatFoldersChangedNotification(1), CancellationToken.None);

        phone.Verify(s => s.WriteAsync(It.IsAny<ChatFoldersChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        desktop.Verify(s => s.WriteAsync(It.IsAny<ChatFoldersChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        otherUser.Verify(s => s.WriteAsync(It.IsAny<ChatFoldersChangedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_BrokenStream_DoesNotBlockOtherDevices()
    {
        var broken = new Mock<IServerStreamWriter<ChatFoldersChangedEvent>>();
        broken.Setup(s => s.WriteAsync(It.IsAny<ChatFoldersChangedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "gone")));
        var healthy = new Mock<IServerStreamWriter<ChatFoldersChangedEvent>>();
        _subscriptions.RegisterSubscription(1, broken.Object);
        _subscriptions.RegisterSubscription(1, healthy.Object);

        await Handler().Handle(new ChatFoldersChangedNotification(1), CancellationToken.None);

        healthy.Verify(s => s.WriteAsync(It.IsAny<ChatFoldersChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
