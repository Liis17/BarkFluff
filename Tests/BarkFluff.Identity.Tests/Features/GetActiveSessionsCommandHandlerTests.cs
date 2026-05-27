using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Features.GetActiveSessions;
using BarkFluff.Identity.Features.GetActiveSessionsServer;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Users;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Identity.Tests.Features;

public class GetActiveSessionsCommandHandlerTests
{
    private readonly IdentityContext _context;
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly UserContext _userContext;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<ILogger<GetActiveSessionsCommandHandler>> _logger;

    public GetActiveSessionsCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _refreshTokensStorage = new RefreshTokensStorage(_context);
        _userContext = TestHelper.CreateUserContext(1);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _logger = new Mock<ILogger<GetActiveSessionsCommandHandler>>();
    }

    [Fact]
    public async Task Handle_NoSessions_ReturnsEmptyList()
    {
        _usersClient
            .Setup(c => c.GetUserDevicesAsync(It.IsAny<GetUserDevicesRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserDevicesResponse>(
                Task.FromResult(new GetUserDevicesResponse()),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = new GetActiveSessionsCommandHandler(
            _refreshTokensStorage, _userContext, _usersClient.Object, _logger.Object);
        var cmd = new GetActiveSessionsCommand();

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Empty(result.Sessions);
    }

    [Fact]
    public async Task Handle_WithSessionsAndDevices_MergesDeviceMetadata()
    {
        _context.RefreshTokens.AddRange(
            new Domain.RefreshToken { Id = 1, Value = "t1", UserId = 1, DeviceId = "dev1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) },
            new Domain.RefreshToken { Id = 2, Value = "t2", UserId = 1, DeviceId = "dev2", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) }
        );
        _context.SaveChanges();

        var devicesResponse = new GetUserDevicesResponse();
        devicesResponse.Devices.Add(new Device { DeviceId = "dev1", OriginalName = "Chrome", CustomName = "My Chrome", AppName = "BF v1", OperationSystem = "Win", Location = "Russia" });
        devicesResponse.Devices.Add(new Device { DeviceId = "dev2", OriginalName = "Firefox", CustomName = "", AppName = "BF v2", OperationSystem = "Linux", Location = "USA" });

        _usersClient
            .Setup(c => c.GetUserDevicesAsync(It.IsAny<GetUserDevicesRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserDevicesResponse>(
                Task.FromResult(devicesResponse),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = new GetActiveSessionsCommandHandler(
            _refreshTokensStorage, _userContext, _usersClient.Object, _logger.Object);
        var cmd = new GetActiveSessionsCommand();

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Equal(2, result.Sessions.Count);
        Assert.Equal("Chrome", result.Sessions[0].OriginalName);
        Assert.Equal("My Chrome", result.Sessions[0].CustomName);
        Assert.Equal("Russia", result.Sessions[0].Location);
        Assert.Equal("Firefox", result.Sessions[1].OriginalName);
    }

    [Fact]
    public async Task Handle_DevicesServiceFails_StillReturnsSessions()
    {
        _context.RefreshTokens.Add(new Domain.RefreshToken { Id = 1, Value = "t1", UserId = 1, DeviceId = "dev1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        _context.SaveChanges();

        _usersClient
            .Setup(c => c.GetUserDevicesAsync(It.IsAny<GetUserDevicesRequest>(), null, null, CancellationToken.None))
            .Throws(new Exception("Service unavailable"));

        var handler = new GetActiveSessionsCommandHandler(
            _refreshTokensStorage, _userContext, _usersClient.Object, _logger.Object);
        var cmd = new GetActiveSessionsCommand();

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Single(result.Sessions);
        Assert.Equal("dev1", result.Sessions[0].DeviceId);
        Assert.Empty(result.Sessions[0].OriginalName);
    }
}

public class GetActiveSessionsServerCommandHandlerTests
{
    private readonly IdentityContext _context;
    private readonly RefreshTokensStorage _refreshTokensStorage;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<ILogger<GetActiveSessionsServerCommandHandler>> _logger;

    public GetActiveSessionsServerCommandHandlerTests()
    {
        _context = TestHelper.CreateContext();
        _refreshTokensStorage = new RefreshTokensStorage(_context);
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _logger = new Mock<ILogger<GetActiveSessionsServerCommandHandler>>();
    }

    [Fact]
    public async Task Handle_ReturnsSessionsForUserId()
    {
        _context.RefreshTokens.Add(new Domain.RefreshToken { Value = "t1", UserId = 42, DeviceId = "d1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        _context.SaveChanges();

        _usersClient
            .Setup(c => c.GetUserDevicesAsync(It.IsAny<GetUserDevicesRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserDevicesResponse>(
                Task.FromResult(new GetUserDevicesResponse()),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = new GetActiveSessionsServerCommandHandler(
            _refreshTokensStorage, _usersClient.Object, _logger.Object);
        var cmd = new GetActiveSessionsServerCommand { UserId = 42 };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Single(result.Sessions);
    }

    [Fact]
    public async Task Handle_NoSessions_ReturnsEmptyList()
    {
        _usersClient
            .Setup(c => c.GetUserDevicesAsync(It.IsAny<GetUserDevicesRequest>(), null, null, CancellationToken.None))
            .Returns(new AsyncUnaryCall<GetUserDevicesResponse>(
                Task.FromResult(new GetUserDevicesResponse()),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var handler = new GetActiveSessionsServerCommandHandler(
            _refreshTokensStorage, _usersClient.Object, _logger.Object);
        var cmd = new GetActiveSessionsServerCommand { UserId = 42 };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Empty(result.Sessions);
    }

    [Fact]
    public async Task Handle_DevicesServiceFails_StillReturnsSessions()
    {
        _context.RefreshTokens.Add(new Domain.RefreshToken { Value = "t1", UserId = 42, DeviceId = "d1", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        _context.SaveChanges();

        _usersClient
            .Setup(c => c.GetUserDevicesAsync(It.IsAny<GetUserDevicesRequest>(), null, null, CancellationToken.None))
            .Throws(new Exception("Service unavailable"));

        var handler = new GetActiveSessionsServerCommandHandler(
            _refreshTokensStorage, _usersClient.Object, _logger.Object);
        var cmd = new GetActiveSessionsServerCommand { UserId = 42 };

        var result = await handler.Handle(cmd, CancellationToken.None);

        Assert.Single(result.Sessions);
        Assert.Equal("d1", result.Sessions[0].DeviceId);
    }
}
