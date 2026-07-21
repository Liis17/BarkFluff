using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Host;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;

using Grpc.Core;

using Microsoft.EntityFrameworkCore;

using Moq;

namespace BarkFluff.Federation.Tests.Host;

public class DeliverEventsTests
{
    private const string Origin = "peer.test";

    private static (FederationContext Context, FederationS2SApiService Service) Create()
    {
        var context = TestHelpers.CreateContext();
        var service = new FederationS2SApiService(
            TestHelpers.CreateConfiguration(),
            TestHelpers.CreateSigningKeyService(context),
            Mock.Of<UsersServerApi.UsersServerApiClient>(),
            Mock.Of<MessagesServerApi.MessagesServerApiClient>(),
            context,
            new MetricsCollector());
        return (context, service);
    }

    private static (FederationContext Context, FederationS2SApiService Service, Mock<MessagesServerApi.MessagesServerApiClient> MessagesMock) CreateWithMessagesMock()
    {
        var context = TestHelpers.CreateContext();
        var messagesMock = new Mock<MessagesServerApi.MessagesServerApiClient>();
        var service = new FederationS2SApiService(
            TestHelpers.CreateConfiguration(),
            TestHelpers.CreateSigningKeyService(context),
            Mock.Of<UsersServerApi.UsersServerApiClient>(),
            messagesMock.Object,
            context,
            new MetricsCollector());
        return (context, service, messagesMock);
    }

    // Ключ «origin-ноды»: генерируем честную пару и сидируем публичную часть в KnownServerKeys.
    private static async Task<FederationSigningKey> SeedOriginKeyAsync(FederationContext context, string origin = Origin)
    {
        var key = await TestHelpers.EnsureActiveKeyAsync(context);
        context.KnownServerKeys.Add(new KnownServerKey
        {
            ServerName = origin,
            KeyId = key.KeyId,
            PublicKey = key.PublicKey,
        });
        await context.SaveChangesAsync();
        return key;
    }

    private static FederationEvent SignedNewMessage(FederationSigningKey key, string origin = Origin, string? authorServer = null)
    {
        var evt = new FederationEvent
        {
            EventId = Guid.NewGuid().ToString(),
            OriginServer = origin,
            OriginTsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            NewMessage = new NewMessagePayload
            {
                ChatId = Guid.NewGuid().ToString(),
                FederatedMessageId = Guid.NewGuid().ToString(),
                Sender = new FederatedUser
                {
                    Uuid = Guid.NewGuid().ToString(),
                    ServerName = authorServer ?? origin,
                },
            },
        };
        EventSigner.Sign(evt, key);
        return evt;
    }

    private static async Task<EventResult> DeliverOneAsync(FederationS2SApiService service, FederationEvent evt, string? origin = Origin)
    {
        var request = new DeliverEventsRequest();
        request.Events.Add(evt);
        var response = await service.DeliverEvents(request, TestHelpers.CreateCallContext(origin));
        return response.Results.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task DeliverEvents_MissingOrigin_AllRejected()
    {
        var (_, service) = Create();
        var request = new DeliverEventsRequest();
        request.Events.Add(new FederationEvent { EventId = Guid.NewGuid().ToString(), OriginServer = Origin });
        request.Events.Add(new FederationEvent { EventId = Guid.NewGuid().ToString(), OriginServer = Origin });

        var response = await service.DeliverEvents(request, TestHelpers.CreateCallContext(xfedOrigin: null));

        response.Results.Should().HaveCount(2);
        response.Results.Should().OnlyContain(r => r.Status == EventStatus.Rejected && r.ErrorCode == "missing_origin");
    }

    [Fact]
    public async Task DeliverEvents_InvalidEventId_Rejected()
    {
        var (_, service) = Create();
        var evt = new FederationEvent { EventId = "not-a-guid", OriginServer = Origin };

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Rejected);
        result.ErrorCode.Should().Be("invalid_event_id");
    }

    [Fact]
    public async Task DeliverEvents_OriginMismatch_Rejected()
    {
        var (context, service) = Create();
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedNewMessage(key);
        evt.OriginServer = "someone-else.test"; // подпись валидна, но заявленный origin не совпадает с x-bf-origin

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Rejected);
        result.ErrorCode.Should().Be("origin_mismatch");
    }

    [Fact]
    public async Task DeliverEvents_AlreadyProcessed_Deduplicated()
    {
        var (context, service) = Create();
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedNewMessage(key);
        context.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = Guid.Parse(evt.EventId),
            OriginServer = Origin,
            ReceivedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.AlreadyProcessed);
    }

    [Fact]
    public async Task DeliverEvents_UnknownKey_Rejected()
    {
        var (context, service) = Create();
        var key = await TestHelpers.EnsureActiveKeyAsync(context); // KnownServerKeys не сидируем
        var evt = SignedNewMessage(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Rejected);
        result.ErrorCode.Should().Be("unknown_key");
    }

    [Fact]
    public async Task DeliverEvents_RevokedKey_Rejected()
    {
        var (context, service) = Create();
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedNewMessage(key);
        context.KnownServerKeys.Single(k => k.KeyId == key.KeyId).RevokedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Rejected);
        result.ErrorCode.Should().Be("unknown_key");
    }

    [Fact]
    public async Task DeliverEvents_ExpiredKey_Rejected()
    {
        var (context, service) = Create();
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedNewMessage(key);
        context.KnownServerKeys.Single(k => k.KeyId == key.KeyId).ExpiredAt = DateTime.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Rejected);
        result.ErrorCode.Should().Be("unknown_key");
    }

    [Fact]
    public async Task DeliverEvents_TamperedPayload_InvalidSignature()
    {
        var (context, service) = Create();
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedNewMessage(key);
        evt.NewMessage.Text = "подменено после подписи"; // подпись перестаёт сходиться

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Rejected);
        result.ErrorCode.Should().Be("invalid_signature");
    }

    [Fact]
    public async Task DeliverEvents_AuthorNotOrigin_Rejected()
    {
        var (context, service) = Create();
        var key = await SeedOriginKeyAsync(context);
        // Нода говорит только за своих: автор внутри payload принадлежит чужой ноде.
        var evt = SignedNewMessage(key, authorServer: "victim.test");

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Rejected);
        result.ErrorCode.Should().Be("author_not_origin");
    }

    [Fact]
    public async Task DeliverEvents_ValidChatEvent_RoutedToMessagesAndIndexed()
    {
        // Этап 2.3: chat payload маршрутизируется в MessagesServerApi.ImportFederatedMessage.
        // При OK событие индексируется в ProcessedEvents (идемпотентность).
        var (context, service, messagesMock) = CreateWithMessagesMock();
        SetupMessagesImport(messagesMock, new ImportFederatedMessageResponse());

        var key = await SeedOriginKeyAsync(context);
        var evt = SignedNewMessage(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Ok);
        (await context.ProcessedEvents.CountAsync()).Should().Be(1);
        messagesMock.Verify(c => c.ImportFederatedMessageAsync(
            It.IsAny<ImportFederatedMessageRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeliverEvents_MessagesTransient_RetryAndNotIndexed()
    {
        // Если Messages временно недоступен (Unavailable) → RETRY, идемпотентность не пишется.
        var (context, service, messagesMock) = CreateWithMessagesMock();
        messagesMock
            .Setup(c => c.ImportFederatedMessageAsync(
                It.IsAny<ImportFederatedMessageRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "messages down")));

        var key = await SeedOriginKeyAsync(context);
        var evt = SignedNewMessage(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Retry);
        (await context.ProcessedEvents.CountAsync()).Should().Be(0);
    }

    private static void SetupMessagesImport(Mock<MessagesServerApi.MessagesServerApiClient> mock, ImportFederatedMessageResponse resp)
    {
        mock.Setup(c => c.ImportFederatedMessageAsync(
                It.IsAny<ImportFederatedMessageRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<ImportFederatedMessageResponse>(
                Task.FromResult(resp),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    [Fact]
    public async Task DeliverEvents_UnknownPayloadType_Rejected()
    {
        var (context, service) = Create();
        var key = await SeedOriginKeyAsync(context);
        var evt = new FederationEvent
        {
            EventId = Guid.NewGuid().ToString(),
            OriginServer = Origin,
            OriginTsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        EventSigner.Sign(evt, key); // payload не задан

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Rejected);
    }
}
