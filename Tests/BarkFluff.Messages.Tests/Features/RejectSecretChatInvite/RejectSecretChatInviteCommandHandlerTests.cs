using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.RejectSecretChatInvite;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Messages.Persistence.Services.Dtos;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.RejectSecretChatInvite;

public class RejectSecretChatInviteCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<SecretMessageBuffer> _buffer;
    private readonly Mock<SecretMessageQueueSender> _queueSender;

    public RejectSecretChatInviteCommandHandlerTests()
    {
        _buffer = new Mock<SecretMessageBuffer>(Mock.Of<StackExchange.Redis.IConnectionMultiplexer>(), TestHelper.CreateLogger<SecretMessageBuffer>());
        _queueSender = new Mock<SecretMessageQueueSender>(_h.PublishEndpointMock.Object);
    }

    private RejectSecretChatInviteCommandHandler CreateHandler(long userId, string? deviceId = "00000000-0000-0000-0000-000000000001")
    {
        return new RejectSecretChatInviteCommandHandler(
            _buffer.Object,
            _queueSender.Object,
            _h.CreateUserContext(userId, deviceId),
            _h.Metrics,
            TestHelper.CreateLogger<RejectSecretChatInviteCommandHandler>());
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_ValidReject_ReturnsResponse()
    {
        var deviceId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var invite = new SecretInviteRecord
        {
            InviteId = "invite-1",
            SenderUserId = 10,
            SenderDeviceId = Guid.NewGuid(),
            RecipientUserId = 1,
            RecipientDeviceId = deviceId
        };
        _buffer.Setup(b => b.ConsumeInviteAsync(deviceId, "invite-1")).ReturnsAsync(invite);
        var handler = CreateHandler(1);

        var result = await handler.Handle(new RejectSecretChatInviteCommand { InviteId = "invite-1" }, CancellationToken.None);

        result.Should().NotBeNull();
        _queueSender.Verify(q => q.SendInviteResolution("invite-1", 10, invite.SenderDeviceId, 1, deviceId, false, It.IsAny<byte[]>()), Times.Once);
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_NoDeviceId_ThrowsDeviceIdRequiredException()
    {
        var handler = CreateHandler(1, deviceId: null);

        var act = async () => await handler.Handle(new RejectSecretChatInviteCommand { InviteId = "invite-1" }, CancellationToken.None);

        await act.Should().ThrowAsync<DeviceIdRequiredException>();
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_EmptyInviteId_ThrowsSecretInviteNotFoundException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new RejectSecretChatInviteCommand { InviteId = "" }, CancellationToken.None);

        await act.Should().ThrowAsync<SecretInviteNotFoundException>();
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_InviteNotFound_ThrowsSecretInviteNotFoundException()
    {
        var deviceId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        _buffer.Setup(b => b.ConsumeInviteAsync(deviceId, "invite-1")).ReturnsAsync((SecretInviteRecord?)null);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new RejectSecretChatInviteCommand { InviteId = "invite-1" }, CancellationToken.None);

        await act.Should().ThrowAsync<SecretInviteNotFoundException>();
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_WrongRecipient_ThrowsNoAccessToChatException()
    {
        var deviceId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var invite = new SecretInviteRecord { RecipientUserId = 99, RecipientDeviceId = deviceId };
        _buffer.Setup(b => b.ConsumeInviteAsync(deviceId, "invite-1")).ReturnsAsync(invite);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new RejectSecretChatInviteCommand { InviteId = "invite-1" }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }
}
