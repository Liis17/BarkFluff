using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.SendSecretMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.SendSecretMessage;

public class SendSecretMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<SecretMessageBuffer> _buffer;
    private readonly Mock<SecretMessageQueueSender> _queueSender;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;

    public SendSecretMessageCommandHandlerTests()
    {
        _buffer = new Mock<SecretMessageBuffer>(Mock.Of<StackExchange.Redis.IConnectionMultiplexer>(), TestHelper.CreateLogger<SecretMessageBuffer>());
        _queueSender = new Mock<SecretMessageQueueSender>(_h.PublishEndpointMock.Object);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        SetupUserStatus(AccountStatus.Active);
    }

    private void SetupUserStatus(AccountStatus status)
    {
        _usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns((GetByIdRequest request, Grpc.Core.Metadata? _, DateTime? _, CancellationToken _) =>
                TestHelper.CreateAsyncCall(new GetByIdResponse { User = new User { Id = request.UserId, AccountStatus = status } }));
    }

    private SendSecretMessageCommandHandler CreateHandler(long userId, string? deviceId = "00000000-0000-0000-0000-000000000001")
    {
        return new SendSecretMessageCommandHandler(
            _buffer.Object,
            _queueSender.Object,
            _usersClient.Object,
            _h.CreateUserContext(userId, deviceId),
            _h.Metrics,
            TestHelper.CreateLogger<SendSecretMessageCommandHandler>());
    }

    [Fact]
    public async Task Handle_RestrictedRecipient_ThrowsBeforeEnqueueingMessage()
    {
        SetupUserStatus(AccountStatus.PendingDeletion);
        var handler = CreateHandler(1);

        var act = () => handler.Handle(new SendSecretMessageCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            Envelope = new byte[100]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<BarkFluff.Shared.Exceptions.Users.UserUnavailableException>();
        _buffer.Verify(b => b.EnqueueMessageAsync(It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task Handle_PublishesMessageWithoutLegacySilentPush()
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
        // CloudMessaging consumes NewSecretMessageEvent; the legacy push would duplicate it.
        _queueSender.Verify(q => q.SendSilentPush(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }
}
