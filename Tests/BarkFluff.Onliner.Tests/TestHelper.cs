using System.Security.Claims;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Domain.Entities;
using BarkFluff.Onliner.Domain.Enums;
using BarkFluff.Onliner.Persistence.Contexts;
using BarkFluff.Onliner.Services;
using BarkFluff.Onliner.Tests.Fakes;
using BarkFluff.Proto.Onliner;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Identity;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BarkFluff.Onliner.Tests;

public class TestHelper
{
    public OnlineStatusContext DbContext { get; }
    public InMemoryPresenceStore Presence { get; }
    public OnlineStatusSubscriptionsManager SubscriptionsManager { get; }
    public OnlineStatusNotifier Notifier { get; }
    public Mock<UsersServerApi.UsersServerApiClient> UsersClientMock { get; }
    public MetricsCollector Metrics { get; }

    public TestHelper()
    {
        var options = new DbContextOptionsBuilder<OnlineStatusContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        DbContext = new OnlineStatusContext(options);
        Presence = new InMemoryPresenceStore();
        SubscriptionsManager = new OnlineStatusSubscriptionsManager();
        Metrics = new MetricsCollector();
        UsersClientMock = new Mock<UsersServerApi.UsersServerApiClient>();

        var subscriptionsManager = SubscriptionsManager;
        Notifier = new OnlineStatusNotifier(
            subscriptionsManager,
            Metrics,
            CreateLogger<OnlineStatusNotifier>());
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

    public static ILogger<T> CreateLogger<T>()
    {
        return Mock.Of<ILogger<T>>();
    }

    public OnlineVisibilityFilter CreateVisibilityFilter()
    {
        return new OnlineVisibilityFilter(
            UsersClientMock.Object,
            Metrics,
            CreateLogger<OnlineVisibilityFilter>());
    }

    public void SetupUserPrivacy(long userId, ProfileFieldVisibility visibility)
    {
        var response = new GetUserPrivacyResponse
        {
            Settings = new PrivacySettings { OnlineVisibility = visibility }
        };

        UsersClientMock
            .Setup(c => c.GetUserPrivacyAsync(
                It.Is<GetUserPrivacyRequest>(r => r.UserId == userId),
                null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetUserPrivacyResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    public void SetupUserPrivacyError(long userId)
    {
        UsersClientMock
            .Setup(c => c.GetUserPrivacyAsync(
                It.Is<GetUserPrivacyRequest>(r => r.UserId == userId),
                null, null, It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "Service unavailable")));
    }

    public Mock<IServerStreamWriter<Proto.Onliner.UserOnlineStatus>> CreateMockStreamWriter()
    {
        return new Mock<IServerStreamWriter<Proto.Onliner.UserOnlineStatus>>();
    }

    public async Task SeedDbStatus(long userId, Domain.Enums.StatusTypeId status, DateTime lastSeen)
    {
        var entity = new Domain.Entities.UserOnlineStatus
        {
            UserId = userId,
            Status = status,
            LastSeen = lastSeen
        };
        DbContext.UsersOnlineStatuses.Add(entity);
        await DbContext.SaveChangesAsync();
    }

    /// <summary>RedisSingleRunner с mock-мультиплексором: методы захвата лока не вызываются в тестах
    /// (проверяем приватный проход напрямую), нужен лишь для конструктора фоновых сервисов.</summary>
    public static RedisSingleRunner CreateSingleRunner() => new(Mock.Of<IConnectionMultiplexer>());

    /// <summary>Scope-factory над тем же in-memory DbContext (фоновые сервисы резолвят контекст в scope).</summary>
    public IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton(DbContext);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
