using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.SendSecretChatInvite;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.SendSecretChatInvite;

public class SendSecretChatInviteCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<SecretMessageBuffer> _buffer;
    private readonly Mock<SecretMessageQueueSender> _queueSender;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;

    public SendSecretChatInviteCommandHandlerTests()
    {
        _buffer = new Mock<SecretMessageBuffer>(Mock.Of<StackExchange.Redis.IConnectionMultiplexer>(), TestHelper.CreateLogger<SecretMessageBuffer>());
        _queueSender = new Mock<SecretMessageQueueSender>(_h.PublishEndpointMock.Object);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        SetupUserStatus(AccountStatus.Active);
    }

    private void SetupUserStatus(AccountStatus status)
    {
        _usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns((GetByIdRequest request, Metadata? _, DateTime? _, CancellationToken _) =>
                TestHelper.CreateAsyncCall(new GetByIdResponse { User = new User { Id = request.UserId, AccountStatus = status } }));
    }

    private SendSecretChatInviteCommandHandler CreateHandler(long userId, string? deviceId = "00000000-0000-0000-0000-000000000001")
    {
        return new SendSecretChatInviteCommandHandler(
            _buffer.Object,
            _queueSender.Object,
            _usersClient.Object,
            _h.CreateUserContext(userId, deviceId),
            _h.Metrics,
            TestHelper.CreateLogger<SendSecretChatInviteCommandHandler>());
    }

    [Fact]
    public async Task Handle_RestrictedRecipient_ThrowsBeforeCreatingInvite()
    {
        SetupUserStatus(AccountStatus.Blocked);
        var handler = CreateHandler(1);

        var act = () => handler.Handle(new SendSecretChatInviteCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            InitialEnvelope = new byte[100]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<BarkFluff.Shared.Exceptions.Users.UserUnavailableException>();
        _buffer.Verify(b => b.EnqueueInviteAsync(It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidInvite_SendsAndReturnsResponse()
    {
        _buffer.Setup(b => b.EnqueueInviteAsync(It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<byte[]>()))
            .ReturnsAsync(("invite-1", DateTime.UtcNow.AddHours(24)));
        var handler = CreateHandler(1);

        var result = await handler.Handle(new SendSecretChatInviteCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            InitialEnvelope = new byte[100]
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.InviteId.Should().Be("invite-1");
    }

    [Fact]
    public async Task Handle_NoDeviceId_ThrowsDeviceIdRequiredException()
    {
        var handler = CreateHandler(1, deviceId: null);

        var act = async () => await handler.Handle(new SendSecretChatInviteCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            InitialEnvelope = new byte[100]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<DeviceIdRequiredException>();
    }

    [Fact]
    public async Task Handle_SelfInviteSameDevice_ThrowsSourceForSendMessageNotSetException()
    {
        var deviceId = Guid.NewGuid();
        var handler = CreateHandler(1, deviceId.ToString());

        var act = async () => await handler.Handle(new SendSecretChatInviteCommand
        {
            RecipientUserId = 1,
            RecipientDeviceId = deviceId,
            InitialEnvelope = new byte[100]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<SourceForSendMessageNotSetException>();
    }

    [Fact]
    public async Task Handle_EnvelopeTooSmall_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendSecretChatInviteCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            InitialEnvelope = new byte[10]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_EnvelopeTooLarge_ThrowsInvalidEncryptedPayloadException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendSecretChatInviteCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            InitialEnvelope = new byte[17 * 1024]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidEncryptedPayloadException>();
    }

    [Fact]
    public async Task Handle_PublishesInviteAndSilentPush()
    {
        _buffer.Setup(b => b.EnqueueInviteAsync(It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<byte[]>()))
            .ReturnsAsync(("invite-1", DateTime.UtcNow.AddHours(24)));
        var handler = CreateHandler(1);

        await handler.Handle(new SendSecretChatInviteCommand
        {
            RecipientUserId = 2,
            RecipientDeviceId = Guid.NewGuid(),
            InitialEnvelope = new byte[100]
        }, CancellationToken.None);

        _queueSender.Verify(q => q.SendInvite(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<DateTime>()), Times.Once);
        _queueSender.Verify(q => q.SendSilentPush(2, It.IsAny<string>()), Times.Once);
    }
}
