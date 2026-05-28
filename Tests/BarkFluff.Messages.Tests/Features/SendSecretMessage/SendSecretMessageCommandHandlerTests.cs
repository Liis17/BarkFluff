using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.SendSecretMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.SendSecretMessage;

public class SendSecretMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<SecretMessageBuffer> _buffer;
    private readonly Mock<SecretMessageQueueSender> _queueSender;

    public SendSecretMessageCommandHandlerTests()
    {
        _buffer = new Mock<SecretMessageBuffer>(Mock.Of<StackExchange.Redis.IConnectionMultiplexer>(), TestHelper.CreateLogger<SecretMessageBuffer>());
        _queueSender = new Mock<SecretMessageQueueSender>(_h.PublishEndpointMock.Object);
    }

    private SendSecretMessageCommandHandler CreateHandler(long userId, string? deviceId = "00000000-0000-0000-0000-000000000001")
    {
        return new SendSecretMessageCommandHandler(
            _buffer.Object,
            _queueSender.Object,
            _h.CreateUserContext(userId, deviceId),
            _h.Metrics,
            TestHelper.CreateLogger<SendSecretMessageCommandHandler>());
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_ValidMessage_SendsAndReturnsResponse()
    {
        _buffer.Setup(b => b.EnqueueMessageAsync(It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<byte[]>()))
            .ReturnsAsync(("msg-1", DateTime.UtcNow.AddHours(24)));
        var handler = CreateHandler(1);

        var result = await handler.Handle(new SendSecretMessageCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            Envelope = new byte[100]
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.MessageId.Should().Be("msg-1");
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_NoDeviceId_ThrowsDeviceIdRequiredException()
    {
        var handler = CreateHandler(1, deviceId: null);

        var act = async () => await handler.Handle(new SendSecretMessageCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            Envelope = new byte[100]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<DeviceIdRequiredException>();
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_EnvelopeTooSmall_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendSecretMessageCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            Envelope = new byte[10]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_EnvelopeTooLarge_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendSecretMessageCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            Envelope = new byte[17 * 1024]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_PublishesMessageAndSilentPush()
    {
        _buffer.Setup(b => b.EnqueueMessageAsync(It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<byte[]>()))
            .ReturnsAsync(("msg-1", DateTime.UtcNow.AddHours(24)));
        var handler = CreateHandler(1);

        await handler.Handle(new SendSecretMessageCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            Envelope = new byte[100]
        }, CancellationToken.None);

        _queueSender.Verify(q => q.SendMessage(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<DateTime>()), Times.Once);
        _queueSender.Verify(q => q.SendSilentPush(2, It.IsAny<string>()), Times.Once);
    }
}
