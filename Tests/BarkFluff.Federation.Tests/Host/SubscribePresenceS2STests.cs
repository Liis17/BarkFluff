using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Host;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Onliner;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Federation;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using OnlinerStatusTypeId = BarkFluff.Proto.Onliner.StatusTypeId;

namespace BarkFluff.Federation.Tests.Host;

/// <summary>
/// Origin-сторона presence-моста (этап 4.3): проверка отношений, лимиты, privacy, блоклист.
/// </summary>
public class SubscribePresenceS2STests
{
    private const string Origin = "node-b.test";

    private sealed class CollectingStreamWriter : IServerStreamWriter<PresenceEvent>
    {
        public List<PresenceEvent> Written { get; } = [];

        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(PresenceEvent message)
        {
            Written.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteAsync(PresenceEvent message, CancellationToken cancellationToken)
        {
            Written.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed record Harness(
        FederationS2SApiService Service,
        FederationContext Context,
        Mock<MessagesServerApi.MessagesServerApiClient> Messages,
        Mock<UsersServerApi.UsersServerApiClient> Users,
        Mock<OnlinerServerApi.OnlinerServerApiClient> Onliner,
        IncomingPresenceRegistry Registry);

    private static Harness CreateHarness(
        IDictionary<string, string?>? configOverrides = null,
        int maxSubscriptionSize = 500)
    {
        var overrides = new Dictionary<string, string?>(configOverrides ?? new Dictionary<string, string?>())
        {
            ["Federation:MaxPresenceSubscriptionSize"] = maxSubscriptionSize.ToString(),
            // Мелкое окно coalescing → тик цикла 250 мс, тест не ждёт секундами.
            ["Federation:PresenceCoalesceSeconds"] = "1",
        };

        var configuration = TestHelpers.CreateConfiguration(overrides);
        var context = TestHelpers.CreateContext();
        var registry = new IncomingPresenceRegistry();

        var messages = new Mock<MessagesServerApi.MessagesServerApiClient>();
        var users = new Mock<UsersServerApi.UsersServerApiClient>();
        var onliner = new Mock<OnlinerServerApi.OnlinerServerApiClient>();

        var service = new FederationS2SApiService(
            configuration,
            TestHelpers.CreateSigningKeyService(context, configuration),
            users.Object,
            messages.Object,
            onliner.Object,
            context,
            new FederationSwitch(configuration),
            registry,
            new PresenceOptions(configuration),
            new FakeTypingRateLimiter(),
            new TypingValidationCache(new PresenceOptions(configuration)),
            new MetricsCollector(),
            new FakeChatCreatedQuotaLimiter(),
            NullLogger<FederationS2SApiService>.Instance);

        return new Harness(service, context, messages, users, onliner, registry);
    }

    private static void SetupAccess(Harness harness, params string[] allowedUuids)
    {
        var response = new CheckFederatedPresenceAccessResponse();
        response.AllowedUserUuids.AddRange(allowedUuids);

        harness.Messages
            .Setup(c => c.CheckFederatedPresenceAccessAsync(
                It.IsAny<CheckFederatedPresenceAccessRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.UnaryCall(response));
    }

    private static void SetupUsers(Harness harness, params (Guid Uuid, long UserId)[] users)
    {
        var response = new GetUsersByUuidResponse();
        foreach (var (uuid, userId) in users)
        {
            response.Users.Add(new UserProfileByUuid
            {
                Uuid = uuid.ToString(),
                Found = true,
                IsRemote = false,
                UserId = userId,
            });
        }

        harness.Users
            .Setup(c => c.GetUsersByUuidAsync(
                It.IsAny<GetUsersByUuidRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.UnaryCall(response));
    }

    private static void SetupPresence(Harness harness, params (long UserId, OnlinerStatusTypeId Status)[] statuses)
    {
        var response = new GetLocalPresenceResponse();
        foreach (var (userId, status) in statuses)
        {
            response.Statuses.Add(new UserOnlineStatus
            {
                UserId = userId,
                Status = status,
                LastSeen = Timestamp.FromDateTime(DateTime.UtcNow),
            });
        }

        harness.Onliner
            .Setup(c => c.GetLocalPresenceAsync(
                It.IsAny<GetLocalPresenceRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.UnaryCall(response));
    }

    private static async Task<CollectingStreamWriter> RunAsync(
        Harness harness,
        SubscribePresenceRequest request,
        int millis = 900)
    {
        var writer = new CollectingStreamWriter();
        using var cts = new CancellationTokenSource(millis);

        await harness.Service.SubscribePresence(
            request, writer, TestHelpers.CreateCallContext(Origin, cts.Token));

        return writer;
    }

    private static SubscribePresenceRequest RequestFor(params Guid[] uuids)
    {
        var request = new SubscribePresenceRequest();
        request.UserUuids.AddRange(uuids.Select(u => u.ToString()));
        return request;
    }

    [Fact]
    public async Task SubscribePresence_FederationDisabled_Throws()
    {
        var harness = CreateHarness(new Dictionary<string, string?> { ["Federation:Enabled"] = "false" });

        var act = async () => await RunAsync(harness, RequestFor(Guid.NewGuid()));

        await act.Should().ThrowAsync<FederationNotConfiguredException>();
    }

    [Fact]
    public async Task SubscribePresence_BlockedOrigin_ThrowsPermissionDenied()
    {
        var harness = CreateHarness();
        harness.Context.KnownServers.Add(new KnownServer
        {
            ServerName = Origin,
            FederationEndpoint = "http://localhost:1",
            Source = KnownServerSource.Manual,
            Status = KnownServerStatus.Blocked,
        });
        await harness.Context.SaveChangesAsync();

        var act = async () => await RunAsync(harness, RequestFor(Guid.NewGuid()));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task SubscribePresence_OverLimit_ThrowsResourceExhausted()
    {
        // Не обрезаем молча: на origin-стороне превышение — сигнал злоупотребления.
        var harness = CreateHarness(maxSubscriptionSize: 2);

        var act = async () => await RunAsync(
            harness, RequestFor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.ResourceExhausted);
    }

    [Fact]
    public async Task SubscribePresence_UuidWithoutSharedChat_NeverReachesStream()
    {
        // Риск №42: без общего активного fed-чата статус не отдаётся вовсе.
        var harness = CreateHarness();
        var allowed = Guid.NewGuid();
        var denied = Guid.NewGuid();

        SetupAccess(harness, allowed.ToString());
        SetupUsers(harness, (allowed, 10));
        SetupPresence(harness, (10, OnlinerStatusTypeId.StatusOnline));

        var writer = await RunAsync(harness, RequestFor(allowed, denied));

        writer.Written.Should().NotBeEmpty();
        writer.Written.Should().OnlyContain(e => e.UserUuid == allowed.ToString());
        writer.Written.Should().NotContain(e => e.UserUuid == denied.ToString());
    }

    [Fact]
    public async Task SubscribePresence_NoAllowedUuids_StreamOpensAndStaysSilent()
    {
        // Молчание вместо PermissionDenied: иначе подписчик узнал бы, какие uuid существуют.
        var harness = CreateHarness();
        SetupAccess(harness);

        var writer = await RunAsync(harness, RequestFor(Guid.NewGuid()));

        writer.Written.Should().BeEmpty();
        harness.Registry.Count.Should().Be(0, "подписка снимается при закрытии стрима");
    }

    [Fact]
    public async Task SubscribePresence_PrivacyHiddenUser_IsSentAsUnknown()
    {
        // Onliner отдал UNKNOWN (пользователь скрыл онлайн) — наружу обязан уйти именно UNKNOWN,
        // реальный статус не должен «протечь».
        var harness = CreateHarness();
        var uuid = Guid.NewGuid();

        SetupAccess(harness, uuid.ToString());
        SetupUsers(harness, (uuid, 10));
        SetupPresence(harness, (10, OnlinerStatusTypeId.Unknown));

        var writer = await RunAsync(harness, RequestFor(uuid));

        writer.Written.Should().NotBeEmpty();
        writer.Written.Should().OnlyContain(e => e.Status == PresenceStatus.Unknown);
    }

    [Fact]
    public async Task SubscribePresence_SendsInitialSnapshot()
    {
        var harness = CreateHarness();
        var uuid = Guid.NewGuid();

        SetupAccess(harness, uuid.ToString());
        SetupUsers(harness, (uuid, 10));
        SetupPresence(harness, (10, OnlinerStatusTypeId.StatusOnline));

        var writer = await RunAsync(harness, RequestFor(uuid));

        writer.Written.Should().NotBeEmpty();
        writer.Written[0].UserUuid.Should().Be(uuid.ToString());
        writer.Written[0].Status.Should().Be(PresenceStatus.Online);
    }

    [Fact]
    public async Task SubscribePresence_RemoteUserUuid_IsNotResolvedAsOurs()
    {
        // Отдавать в федерацию можно только СВОИХ: remote-профиль в watched не попадает.
        var harness = CreateHarness();
        var uuid = Guid.NewGuid();

        SetupAccess(harness, uuid.ToString());

        var users = new GetUsersByUuidResponse();
        users.Users.Add(new UserProfileByUuid
        {
            Uuid = uuid.ToString(),
            Found = true,
            IsRemote = true,
            ServerName = "node-c.test",
            UserId = 0,
        });
        harness.Users
            .Setup(c => c.GetUsersByUuidAsync(
                It.IsAny<GetUsersByUuidRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.UnaryCall(users));

        var writer = await RunAsync(harness, RequestFor(uuid));

        writer.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task Ping_WhenFederationActive_AdvertisesPresenceCapability()
    {
        var harness = CreateHarness();

        var response = await harness.Service.Ping(new PingRequest(), TestHelpers.CreateCallContext(Origin));

        response.Capabilities.Should().Contain("presence");
    }

    [Fact]
    public async Task Ping_WhenFederationDisabled_AdvertisesNoCapabilities()
    {
        var harness = CreateHarness(new Dictionary<string, string?> { ["Federation:Enabled"] = "false" });

        var response = await harness.Service.Ping(new PingRequest(), TestHelpers.CreateCallContext(Origin));

        response.Capabilities.Should().BeEmpty();
    }
}
