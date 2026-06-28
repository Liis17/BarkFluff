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
