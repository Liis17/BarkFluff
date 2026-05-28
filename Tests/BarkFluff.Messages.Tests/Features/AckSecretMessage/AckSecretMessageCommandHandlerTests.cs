using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.AckSecretMessage;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.AckSecretMessage;

public class AckSecretMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<SecretMessageBuffer> _buffer;

    public AckSecretMessageCommandHandlerTests()
    {
        _buffer = new Mock<SecretMessageBuffer>(Mock.Of<StackExchange.Redis.IConnectionMultiplexer>(), TestHelper.CreateLogger<SecretMessageBuffer>());
    }

    private AckSecretMessageCommandHandler CreateHandler(long userId, string? deviceId = "00000000-0000-0000-0000-000000000001")
    {
        return new AckSecretMessageCommandHandler(
            _buffer.Object,
            _h.CreateUserContext(userId, deviceId),
            _h.Metrics,
            TestHelper.CreateLogger<AckSecretMessageCommandHandler>());
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_ValidAck_ReturnsResponse()
    {
        var deviceId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        _buffer.Setup(b => b.AckMessageAsync(deviceId, "msg-1")).ReturnsAsync(true);
        var handler = CreateHandler(1);

        var result = await handler.Handle(new AckSecretMessageCommand { MessageId = "msg-1" }, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_NoDeviceId_ThrowsDeviceIdRequiredException()
    {
        var handler = CreateHandler(1, deviceId: null);

        var act = async () => await handler.Handle(new AckSecretMessageCommand { MessageId = "msg-1" }, CancellationToken.None);

        await act.Should().ThrowAsync<DeviceIdRequiredException>();
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_EmptyMessageId_ReturnsResponse()
    {
        var handler = CreateHandler(1);

        var result = await handler.Handle(new AckSecretMessageCommand { MessageId = "" }, CancellationToken.None);

        result.Should().NotBeNull();
        _buffer.Verify(b => b.AckMessageAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact(Skip = "Requires Redis")]
    public async Task Handle_CallsAckMessageAsync()
    {
        var deviceId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        _buffer.Setup(b => b.AckMessageAsync(deviceId, "msg-1")).ReturnsAsync(true);
        var handler = CreateHandler(1);

        await handler.Handle(new AckSecretMessageCommand { MessageId = "msg-1" }, CancellationToken.None);

        _buffer.Verify(b => b.AckMessageAsync(deviceId, "msg-1"), Times.Once);
    }
}
