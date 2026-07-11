using BarkFluff.Bots.Consumers;
using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Notifications;

using Grpc.Core;

using MassTransit;

using MessagesProto = BarkFluff.Proto.Messages;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Bots.Tests.Consumers;

public class LoginNotificationConsumerTests
{
    private readonly BotRegistryCache _registry = new();
    private readonly Mock<MessagesProto.MessagesServerApi.MessagesServerApiClient> _messagesClient = new();
    private readonly LoginNotificationConsumer _consumer;

    public LoginNotificationConsumerTests()
    {
        _consumer = new LoginNotificationConsumer(
            _registry,
            _messagesClient.Object,
            new MetricsCollector(),
            Mock.Of<ILogger<LoginNotificationConsumer>>());
    }

    [Fact]
    public async Task Consume_SuccessfulLoginForHuman_SendsNotifierMessage()
    {
        _registry.Load(new[] { new Bot { Id = 1, SystemRole = SystemBotRole.LoginNotifier } });
        _messagesClient
            .Setup(c => c.SendMessageServerAsync(It.IsAny<MessagesProto.SendMessageServerRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<MessagesProto.SendMessageResponse>(
                Task.FromResult(new MessagesProto.SendMessageResponse()),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        await _consumer.Consume(CreateContext(new EmailNotification
        {
            OwnerId = 2,
            Type = NotificationType.SuccessfulLogin,
            Payload = new Dictionary<string, string>()
        }).Object);

        _messagesClient.Verify(c => c.SendMessageServerAsync(
            It.Is<MessagesProto.SendMessageServerRequest>(r => r.SenderUserId == 1 && r.UserId == 2),
            null, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Consume_SuccessfulLoginForBot_DoesNotSendNotifierMessage()
    {
        _registry.Load(new[]
        {
            new Bot { Id = 1, SystemRole = SystemBotRole.LoginNotifier },
            new Bot { Id = 2 }
        });

        await _consumer.Consume(CreateContext(new EmailNotification
        {
            OwnerId = 2,
            Type = NotificationType.SuccessfulLogin,
            Payload = new Dictionary<string, string>()
        }).Object);

        _messagesClient.Verify(c => c.SendMessageServerAsync(
            It.IsAny<MessagesProto.SendMessageServerRequest>(), null, null, CancellationToken.None), Times.Never);
    }

    private static Mock<ConsumeContext<EmailNotification>> CreateContext(EmailNotification notification)
    {
        var context = new Mock<ConsumeContext<EmailNotification>>();
        context.Setup(c => c.Message).Returns(notification);
        return context;
    }
}
