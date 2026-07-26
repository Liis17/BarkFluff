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

using Grpc.Core;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BarkFluff.Federation.Tests.Host;

/// <summary>
/// Приём typing от ноды-партнёра (этап 4.4): rate limit, валидация авторства и членства, кеш.
/// </summary>
public class DeliverTypingS2STests
{
    private const string Origin = "node-b.test";
    private static readonly string ChatId = Guid.NewGuid().ToString();

    private sealed record Harness(
        FederationS2SApiService Service,
        FederationContext Context,
        Mock<MessagesServerApi.MessagesServerApiClient> Messages,
        Mock<UsersServerApi.UsersServerApiClient> Users,
        Mock<OnlinerServerApi.OnlinerServerApiClient> Onliner);

    private static Harness CreateHarness(
        IDictionary<string, string?>? configOverrides = null,
        ITypingRateLimiter? rateLimiter = null)
    {
        var configuration = TestHelpers.CreateConfiguration(configOverrides);
        var context = TestHelpers.CreateContext();
        var options = new PresenceOptions(configuration);

        var messages = new Mock<MessagesServerApi.MessagesServerApiClient>();
        var users = new Mock<UsersServerApi.UsersServerApiClient>();
        var onliner = new Mock<OnlinerServerApi.OnlinerServerApiClient>();

        onliner
            .Setup(c => c.InjectRemoteTypingAsync(
                It.IsAny<InjectRemoteTypingRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.UnaryCall(new InjectRemoteTypingResponse()));

        var service = new FederationS2SApiService(
            configuration,
            TestHelpers.CreateSigningKeyService(context, configuration),
            users.Object,
            messages.Object,
            onliner.Object,
            context,
            new FederationSwitch(configuration),
            new IncomingPresenceRegistry(),
            options,
            rateLimiter ?? new FakeTypingRateLimiter(),
            new TypingValidationCache(options),
            new MetricsCollector(),
            new FakeChatCreatedQuotaLimiter(),
            NullLogger<FederationS2SApiService>.Instance);

        return new Harness(service, context, messages, users, onliner);
    }

    private static void SetupSender(Harness harness, Guid uuid, string serverName, bool found = true)
    {
        var response = new GetUsersByUuidResponse();
        response.Users.Add(new UserProfileByUuid
        {
            Uuid = uuid.ToString(),
            Found = found,
            IsRemote = true,
            ServerName = serverName,
        });

        harness.Users
            .Setup(c => c.GetUsersByUuidAsync(
                It.IsAny<GetUsersByUuidRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.UnaryCall(response));
    }

    private static void SetupMembership(Harness harness, bool isMember)
    {
        var response = new CheckChatMembershipResponse();
        if (isMember)
        {
            response.MemberChatIds.Add(ChatId);
        }

        harness.Messages
            .Setup(c => c.CheckChatMembershipAsync(
                It.IsAny<CheckChatMembershipRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.UnaryCall(response));
    }

    private static DeliverTypingRequest Request(Guid senderUuid) => new()
    {
        ChatId = ChatId,
        SenderUuid = senderUuid.ToString(),
        Action = (int)TypingAction.Typing,
    };

    private static Task<DeliverTypingResponse> CallAsync(Harness harness, DeliverTypingRequest request)
        => harness.Service.DeliverTyping(request, TestHelpers.CreateCallContext(Origin));

    [Fact]
    public async Task DeliverTyping_ValidRequest_InjectsIntoOnliner()
    {
        var harness = CreateHarness();
        var sender = Guid.NewGuid();
        SetupSender(harness, sender, Origin);
        SetupMembership(harness, isMember: true);

        await CallAsync(harness, Request(sender));

        harness.Onliner.Verify(c => c.InjectRemoteTypingAsync(
            It.Is<InjectRemoteTypingRequest>(r => r.ChatId == ChatId && r.UserUuid == sender.ToString()),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeliverTyping_AuthorFromAnotherNode_IsRejected()
    {
        // «Нода говорит только за своих»: origin не может инжектить набор от чужого автора.
        var harness = CreateHarness();
        var sender = Guid.NewGuid();
        SetupSender(harness, sender, "node-c.test");
        SetupMembership(harness, isMember: true);

        var act = async () => await CallAsync(harness, Request(sender));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
        harness.Onliner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeliverTyping_SenderNotChatMember_IsRejected()
    {
        // Знание chat_id прав не даёт: чужая нода не может инжектить набор в посторонний чат.
        var harness = CreateHarness();
        var sender = Guid.NewGuid();
        SetupSender(harness, sender, Origin);
        SetupMembership(harness, isMember: false);

        var act = async () => await CallAsync(harness, Request(sender));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
        harness.Onliner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeliverTyping_OverRateLimit_ThrowsResourceExhausted()
    {
        var harness = CreateHarness(rateLimiter: new FakeTypingRateLimiter { Limit = 1 });
        var sender = Guid.NewGuid();
        SetupSender(harness, sender, Origin);
        SetupMembership(harness, isMember: true);

        await CallAsync(harness, Request(sender));

        var act = async () => await CallAsync(harness, Request(sender));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.ResourceExhausted);
    }

    [Fact]
    public async Task DeliverTyping_RepeatedHeartbeat_UsesValidationCache()
    {
        // Heartbeat идёт каждые 4–5с: без кеша каждый стоил бы двух внутренних gRPC-вызовов.
        var harness = CreateHarness();
        var sender = Guid.NewGuid();
        SetupSender(harness, sender, Origin);
        SetupMembership(harness, isMember: true);

        await CallAsync(harness, Request(sender));
        await CallAsync(harness, Request(sender));
        await CallAsync(harness, Request(sender));

        harness.Users.Verify(c => c.GetUsersByUuidAsync(
            It.IsAny<GetUsersByUuidRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
        harness.Messages.Verify(c => c.CheckChatMembershipAsync(
            It.IsAny<CheckChatMembershipRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
        harness.Onliner.Verify(c => c.InjectRemoteTypingAsync(
            It.IsAny<InjectRemoteTypingRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task DeliverTyping_NegativeResultIsCachedToo()
    {
        // Иначе спамящая нода бесплатно нагружала бы Users/Messages на каждом heartbeat'е.
        var harness = CreateHarness();
        var sender = Guid.NewGuid();
        SetupSender(harness, sender, "node-c.test");
        SetupMembership(harness, isMember: true);

        for (var i = 0; i < 3; i++)
        {
            var act = async () => await CallAsync(harness, Request(sender));
            await act.Should().ThrowAsync<RpcException>();
        }

        harness.Users.Verify(c => c.GetUsersByUuidAsync(
            It.IsAny<GetUsersByUuidRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeliverTyping_BlockedOrigin_IsRejected()
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

        var act = async () => await CallAsync(harness, Request(Guid.NewGuid()));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task DeliverTyping_FederationDisabled_Throws()
    {
        var harness = CreateHarness(new Dictionary<string, string?> { ["Federation:Enabled"] = "false" });

        var act = async () => await CallAsync(harness, Request(Guid.NewGuid()));

        await act.Should().ThrowAsync<FederationNotConfiguredException>();
    }

    [Fact]
    public async Task DeliverTyping_MalformedSenderUuid_ThrowsInvalidArgument()
    {
        var harness = CreateHarness();

        var act = async () => await harness.Service.DeliverTyping(
            new DeliverTypingRequest { ChatId = ChatId, SenderUuid = "not-a-uuid" },
            TestHelpers.CreateCallContext(Origin));

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task Ping_AdvertisesTypingCapability()
    {
        var harness = CreateHarness();

        var response = await harness.Service.Ping(new PingRequest(), TestHelpers.CreateCallContext(Origin));

        response.Capabilities.Should().Contain("typing");
    }
}
