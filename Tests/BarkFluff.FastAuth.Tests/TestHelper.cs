using System.Security.Claims;
using BarkFluff.FastAuth.Domain;
using BarkFluff.FastAuth.Tests.Fakes;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Identity;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace BarkFluff.FastAuth.Tests;

public class TestHelper
{
    public InMemoryFastAuthSessionStore Store { get; } = new();
    public InMemoryFastAuthEventBus EventBus { get; } = new();
    public MetricsCollector Metrics { get; } = new();

    public RequestContext CreateRequestContext(
        string? deviceName = "TestDevice",
        string? os = "Windows",
        string? appName = "BarkFluff",
        string? appVersion = "1.0",
        string? ipAddress = "127.0.0.1")
    {
        return new RequestContext
        {
            DeviceName = deviceName,
            OperationSystem = os,
            AppName = appName,
            AppVersion = appVersion,
            IpAddress = ipAddress
        };
    }

    public UserContext CreateUserContext(long userId, string? deviceId = null)
    {
        var claims = new List<Claim>
        {
            new(IdentityClaims.UserId, userId.ToString()),
            new(IdentityClaims.TokenType, "User"),
        };
        if (deviceId != null)
            claims.Add(new Claim(IdentityClaims.DeviceId, deviceId));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        return new UserContext(httpContextAccessor.Object);
    }

    public FastAuthSessionState CreateSession(DateTime? expiresAt = null)
    {
        var now = DateTime.UtcNow;
        var session = new FastAuthSessionState
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = now,
            ExpiresAt = expiresAt ?? now + TimeSpan.FromMinutes(5),
            DeviceName = "TestDevice",
            OperationSystem = "Windows",
            AppName = "BarkFluff",
            AppVersion = "1.0",
            IpAddress = "127.0.0.1"
        };
        Store.Seed(session);
        return session;
    }

    /// <summary>Создаёт сессию и проводит Scan, как это делает ScanFastAuthCommandHandler.</summary>
    public async Task<(FastAuthSessionState Session, string ConfirmationCode)> CreateAndScanSessionAsync(
        long userId = 42, DateTime? expiresAt = null)
    {
        var session = CreateSession(expiresAt);
        var code = Guid.NewGuid().ToString();
        await Store.TryScanAsync(session.Id, userId, code);
        return (await Store.GetAsync(session.Id)!, code);
    }

    public Mock<IdentityServerApi.IdentityServerApiClient> CreateIdentityClientMock()
    {
        return new Mock<IdentityServerApi.IdentityServerApiClient>();
    }

    public void SetupIdentityClientSuccess(
        Mock<IdentityServerApi.IdentityServerApiClient> mock,
        string accessToken = "access_token",
        DateTime? accessTokenExpiresAt = null,
        string refreshToken = "refresh_token",
        DateTime? refreshTokenExpiresAt = null)
    {
        var accessExpiry = Timestamp.FromDateTime(accessTokenExpiresAt ?? DateTime.UtcNow.AddHours(1));
        var refreshExpiry = Timestamp.FromDateTime(refreshTokenExpiresAt ?? DateTime.UtcNow.AddDays(30));

        var response = new CreateSessionForUserServerResponse
        {
            AccessToken = new Token { Value = accessToken, ExpirationDate = accessExpiry },
            RefreshToken = new Token { Value = refreshToken, ExpirationDate = refreshExpiry }
        };

        mock
            .Setup(c => c.CreateSessionForUserServerAsync(
                It.IsAny<CreateSessionForUserServerRequest>(),
                null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<CreateSessionForUserServerResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    public Mock<IServerStreamWriter<FastAuthResult>> CreateMockStreamWriter()
    {
        var mock = new Mock<IServerStreamWriter<FastAuthResult>>();
        mock
            .Setup(s => s.WriteAsync(It.IsAny<FastAuthResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    public static ILogger<T> CreateLogger<T>()
    {
        return Mock.Of<ILogger<T>>();
    }
}
