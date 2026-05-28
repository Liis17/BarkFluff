using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Features.ForceSetPasswordServer;
using BarkFluff.Identity.Features.CreateSessionForUserServer;
using BarkFluff.Identity.Infrastructure;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Notifications;

using Grpc.Core;

using MassTransit;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using User = BarkFluff.Proto.Users.User;

namespace BarkFluff.Identity.Tests.Features;

public class CreateSessionForUserServerCommandHandlerTests
{
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<IMediator> _mediator;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly NotificationQueueSender _notificationSender;
    private readonly IdentityContext _context;
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly LocationClient _locationClient;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<CreateSessionForUserServerCommandHandler>> _logger;

    public CreateSessionForUserServerCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _mediator = new Mock<IMediator>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _notificationSender = new NotificationQueueSender(_publishEndpoint.Object);
        _context = TestHelper.CreateContext();
        _refreshTokensStorage = new RefreshTokensStorage(_context);
        _locationClient = TestHelper.CreateLocationClient();
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<CreateSessionForUserServerCommandHandler>>();

        _mediator.Setup(m => m.Send(It.IsAny<BarkFluff.Identity.Features.CreateToken.CreateTokenCommand>(), CancellationToken.None))
            .ReturnsAsync(new BarkFluff.Proto.Identity.CreateTokenResponse
            {
                AccessToken = new BarkFluff.Proto.Identity.Token { Value = "at" }
            });

        _usersClient
            .Setup(c => c.RegisterDeviceAsync(It.IsAny<RegisterDeviceRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<RegisterDeviceResponse>(
                Task.FromResult(new RegisterDeviceResponse()),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserContactsResponse>(
                Task.FromResult(new GetUserContactsResponse { User = new User { Id = 1, Username = "user" }, Contact = new UserContact { Email = "t@t.com" } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
    }

    private CreateSessionForUserServerCommandHandler CreateHandler()
    {
        return new CreateSessionForUserServerCommandHandler(
            _usersClient.Object, _mediator.Object, _notificationSender,
            _refreshTokensStorage, _locationClient, _metrics, _logger.Object);
    }

    [Fact]
    public async Task Handle_InvalidUserId_ThrowsRpcException()
    {
        var handler = CreateHandler();
        var cmd = new CreateSessionForUserServerCommand { UserId = 0, DeviceId = "d", DeviceName = "n", OperationSystem = "os", AppName = "app" };

        var ex = await Assert.ThrowsAsync<RpcException>(() => handler.Handle(cmd, CancellationToken.None));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Handle_EmptyDeviceId_ThrowsRpcException()
    {
        var handler = CreateHandler();
        var cmd = new CreateSessionForUserServerCommand { UserId = 1, DeviceId = "", DeviceName = "n", OperationSystem = "os", AppName = "app" };

        var ex = await Assert.ThrowsAsync<RpcException>(() => handler.Handle(cmd, CancellationToken.None));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Handle_EmptyDeviceName_ThrowsRpcException()
    {
        var handler = CreateHandler();
        var cmd = new CreateSessionForUserServerCommand { UserId = 1, DeviceId = "d", DeviceName = "", OperationSystem = "os", AppName = "app" };

        var ex = await Assert.ThrowsAsync<RpcException>(() => handler.Handle(cmd, CancellationToken.None));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Handle_EmptyOs_ThrowsRpcException()
    {
        var handler = CreateHandler();
        var cmd = new CreateSessionForUserServerCommand { UserId = 1, DeviceId = "d", DeviceName = "n", OperationSystem = "", AppName = "app" };

        var ex = await Assert.ThrowsAsync<RpcException>(() => handler.Handle(cmd, CancellationToken.None));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Handle_EmptyAppName_ThrowsRpcException()
    {
        var handler = CreateHandler();
        var cmd = new CreateSessionForUserServerCommand { UserId = 1, DeviceId = "d", DeviceName = "n", OperationSystem = "os", AppName = "" };

        var ex = await Assert.ThrowsAsync<RpcException>(() => handler.Handle(cmd, CancellationToken.None));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesSessionAndReturnsTokens()
    {
        var handler = CreateHandler();
        var cmd = new CreateSessionForUserServerCommand
        {
            UserId = 1, DeviceId = "dev1", DeviceName = "MyPhone",
            OperationSystem = "Android", AppName = "BF v1", IpAddress = "1.1.1.1"
        };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("at", result.AccessToken.Value);
        Assert.Empty(_context.RefreshTokens.Where(t => t.DeviceId != "dev1"));
        Assert.Single(_context.RefreshTokens.Where(t => t.DeviceId == "dev1"));
        _usersClient.Verify(c => c.RegisterDeviceAsync(It.IsAny<RegisterDeviceRequest>(), null, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_RegisterDeviceFails_StillSucceeds()
    {
        _usersClient
            .Setup(c => c.RegisterDeviceAsync(It.IsAny<RegisterDeviceRequest>(), null, null, CancellationToken.None))
            .Throws(new Exception("Service unavailable"));

        var handler = CreateHandler();
        var cmd = new CreateSessionForUserServerCommand
        {
            UserId = 1, DeviceId = "dev1", DeviceName = "MyPhone",
            OperationSystem = "Android", AppName = "BF v1", IpAddress = "1.1.1.1"
        };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("at", result.AccessToken.Value);
    }

    [Fact]
    public async Task Handle_NotificationFails_StillSucceeds()
    {
        _usersClient
            .Setup(c => c.GetUserContactsAsync(It.IsAny<GetUserContactsRequest>(), null, null, CancellationToken.None))
            .Throws(new Exception("Service unavailable"));

        var handler = CreateHandler();
        var cmd = new CreateSessionForUserServerCommand
        {
            UserId = 1, DeviceId = "dev1", DeviceName = "MyPhone",
            OperationSystem = "Android", AppName = "BF v1", IpAddress = "1.1.1.1"
        };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("at", result.AccessToken.Value);
    }
}
