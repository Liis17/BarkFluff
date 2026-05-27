using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Features.Logout;
using BarkFluff.Identity.Features.RemoveActiveSession;
using BarkFluff.Identity.Features.RemoveActiveSessionServer;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Settings;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Queue.Identity;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Identity.Tests.Features;

public class RemoveActiveSessionCommandHandlerTests
{
    private readonly IdentityContext _context;
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly UserContext _userContext;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly JwtSettings _jwtSettings;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<RemoveActiveSessionCommandHandler>> _logger;

    public RemoveActiveSessionCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _refreshTokensStorage = new RefreshTokensStorage(_context);
        _userContext = TestHelper.CreateUserContext(1);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _jwtSettings = new JwtSettings { ExpiryMinutes = 30 };
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<RemoveActiveSessionCommandHandler>>();

        _publishEndpoint.Setup(p => p.Publish(It.IsAny<SessionRevokedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _usersClient
            .Setup(c => c.DeleteUserDeviceAsync(It.IsAny<DeleteUserDeviceRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<DeleteUserDeviceResponse>(
                Task.FromResult(new DeleteUserDeviceResponse()),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
    }

    [Fact]
    public async Task Handle_SessionNotFound_ThrowsSessionNotFoundException()
    {
        var handler = new RemoveActiveSessionCommandHandler(
            _refreshTokensStorage, _userContext, _usersClient.Object,
            _publishEndpoint.Object, _jwtSettings, _metrics, _logger.Object);
        var cmd = new RemoveActiveSessionCommand { DeviceId = "dev1" };

        await Assert.ThrowsAsync<SessionNotFoundException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ValidSession_DeletesTokensAndPublishesEvent()
    {
        _context.RefreshTokens.Add(new Domain.RefreshToken { Value = "t1", UserId = 1, DeviceId = "dev1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        _context.SaveChanges();

        var handler = new RemoveActiveSessionCommandHandler(
            _refreshTokensStorage, _userContext, _usersClient.Object,
            _publishEndpoint.Object, _jwtSettings, _metrics, _logger.Object);
        var cmd = new RemoveActiveSessionCommand { DeviceId = "dev1" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<SessionRevokedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _usersClient.Verify(c => c.DeleteUserDeviceAsync(It.IsAny<DeleteUserDeviceRequest>(), null, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_DeviceDeletionFails_StillSucceeds()
    {
        _context.RefreshTokens.Add(new Domain.RefreshToken { Value = "t1", UserId = 1, DeviceId = "dev1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        _context.SaveChanges();

        _usersClient
            .Setup(c => c.DeleteUserDeviceAsync(It.IsAny<DeleteUserDeviceRequest>(), null, null, CancellationToken.None))
            .Throws(new Exception("Service unavailable"));

        var handler = new RemoveActiveSessionCommandHandler(
            _refreshTokensStorage, _userContext, _usersClient.Object,
            _publishEndpoint.Object, _jwtSettings, _metrics, _logger.Object);
        var cmd = new RemoveActiveSessionCommand { DeviceId = "dev1" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
    }
}

public class RemoveActiveSessionServerCommandHandlerTests
{
    private readonly IdentityContext _context;
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly JwtSettings _jwtSettings;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<RemoveActiveSessionServerCommandHandler>> _logger;

    public RemoveActiveSessionServerCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _refreshTokensStorage = new RefreshTokensStorage(_context);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _jwtSettings = new JwtSettings { ExpiryMinutes = 30 };
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<RemoveActiveSessionServerCommandHandler>>();

        _publishEndpoint.Setup(p => p.Publish(It.IsAny<SessionRevokedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _usersClient
            .Setup(c => c.DeleteUserDeviceAsync(It.IsAny<DeleteUserDeviceRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<DeleteUserDeviceResponse>(
                Task.FromResult(new DeleteUserDeviceResponse()),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
    }

    [Fact]
    public async Task Handle_SessionNotFound_ThrowsSessionNotFoundException()
    {
        var handler = new RemoveActiveSessionServerCommandHandler(
            _refreshTokensStorage, _usersClient.Object,
            _publishEndpoint.Object, _jwtSettings, _metrics, _logger.Object);
        var cmd = new RemoveActiveSessionServerCommand { UserId = 42, DeviceId = "dev1" };

        await Assert.ThrowsAsync<SessionNotFoundException>(() => handler.Handle(cmd, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ValidSession_Succeeds()
    {
        _context.RefreshTokens.Add(new Domain.RefreshToken { Value = "t1", UserId = 42, DeviceId = "dev1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        _context.SaveChanges();

        var handler = new RemoveActiveSessionServerCommandHandler(
            _refreshTokensStorage, _usersClient.Object,
            _publishEndpoint.Object, _jwtSettings, _metrics, _logger.Object);
        var cmd = new RemoveActiveSessionServerCommand { UserId = 42, DeviceId = "dev1" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<SessionRevokedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DeviceDeletionFails_StillSucceeds()
    {
        _context.RefreshTokens.Add(new Domain.RefreshToken { Value = "t1", UserId = 42, DeviceId = "dev1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        _context.SaveChanges();

        _usersClient
            .Setup(c => c.DeleteUserDeviceAsync(It.IsAny<DeleteUserDeviceRequest>(), null, null, CancellationToken.None))
            .Throws(new Exception("Service unavailable"));

        var handler = new RemoveActiveSessionServerCommandHandler(
            _refreshTokensStorage, _usersClient.Object,
            _publishEndpoint.Object, _jwtSettings, _metrics, _logger.Object);
        var cmd = new RemoveActiveSessionServerCommand { UserId = 42, DeviceId = "dev1" };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.NotNull(result);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<SessionRevokedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class LogoutCommandHandlerTests
{
    private readonly IdentityContext _context;
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly UserContext _userContext;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<IPublishEndpoint> _publishEndpoint;
    private readonly JwtSettings _jwtSettings;
    private readonly MetricsCollector _metrics;
    private readonly Mock<ILogger<LogoutCommandHandler>> _logger;

    public LogoutCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _refreshTokensStorage = new RefreshTokensStorage(_context);
        _userContext = TestHelper.CreateUserContext(1, "dev1");
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _jwtSettings = new JwtSettings { ExpiryMinutes = 30 };
        _metrics = new MetricsCollector();
        _logger = new Mock<ILogger<LogoutCommandHandler>>();

        _publishEndpoint.Setup(p => p.Publish(It.IsAny<SessionRevokedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _context.RefreshTokens.Add(new Domain.RefreshToken { Value = "t1", UserId = 1, DeviceId = "dev1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        _context.SaveChanges();

        _usersClient
            .Setup(c => c.DeleteUserDeviceAsync(It.IsAny<DeleteUserDeviceRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<DeleteUserDeviceResponse>(
                Task.FromResult(new DeleteUserDeviceResponse()),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
    }

    [Fact]
    public async Task Handle_DeletesTokensAndPublishesEvent()
    {
        var handler = new LogoutCommandHandler(
            _refreshTokensStorage, _userContext, _usersClient.Object,
            _publishEndpoint.Object, _jwtSettings, _metrics, _logger.Object);

        var result = await handler.Handle(new LogoutCommand(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(_context.RefreshTokens);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<SessionRevokedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _usersClient.Verify(c => c.DeleteUserDeviceAsync(It.IsAny<DeleteUserDeviceRequest>(), null, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_DeviceDeletionFails_StillSucceeds()
    {
        _context.RefreshTokens.Add(new Domain.RefreshToken { Value = "t2", UserId = 1, DeviceId = "dev1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        _context.SaveChanges();

        _usersClient
            .Setup(c => c.DeleteUserDeviceAsync(It.IsAny<DeleteUserDeviceRequest>(), null, null, CancellationToken.None))
            .Throws(new Exception("fail"));

        var handler = new LogoutCommandHandler(
            _refreshTokensStorage, _userContext, _usersClient.Object,
            _publishEndpoint.Object, _jwtSettings, _metrics, _logger.Object);

        var result = await handler.Handle(new LogoutCommand(), CancellationToken.None);

        Assert.NotNull(result);
    }
}
