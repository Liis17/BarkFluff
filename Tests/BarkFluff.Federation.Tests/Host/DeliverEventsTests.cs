using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Features.FederationS2SApi;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Onliner;
using BarkFluff.Proto.Users;

using Grpc.Core;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BarkFluff.Federation.Tests.Host;

public class DeliverEventsTests
{
    private const string Origin = "peer.test";

    private static (FederationContext Context, FederationS2SApiHandler Service) Create(IChatCreatedQuotaLimiter? quotaLimiter = null)
    {
        var context = TestHelpers.CreateContext();
        var service = new FederationS2SApiHandler(
            TestHelpers.CreateConfiguration(),
            TestHelpers.CreateSigningKeyService(context),
            Mock.Of<UsersServerApi.UsersServerApiClient>(),
            Mock.Of<MessagesServerApi.MessagesServerApiClient>(),
            Mock.Of<OnlinerServerApi.OnlinerServerApiClient>(),
            context,
            TestHelpers.CreateFederationSwitch(),
            new IncomingPresenceRegistry(),
            TestHelpers.CreatePresenceOptions(),
            new FakeFetchFileRateLimiter(),
            Mock.Of<FilesServerApi.FilesServerApiClient>(),
            new FakeTypingRateLimiter(),
            new TypingValidationCache(TestHelpers.CreatePresenceOptions()),
            new MetricsCollector(),
            quotaLimiter ?? new FakeChatCreatedQuotaLimiter(),
            NullLogger<FederationS2SApiHandler>.Instance);
        return (context, service);
    }

    private static (FederationContext Context, FederationS2SApiHandler Service, Mock<MessagesServerApi.MessagesServerApiClient> MessagesMock) CreateWithMessagesMock(
        IChatCreatedQuotaLimiter? quotaLimiter = null)
    {
        var context = TestHelpers.CreateContext();
        var messagesMock = new Mock<MessagesServerApi.MessagesServerApiClient>();
        var service = new FederationS2SApiHandler(
            TestHelpers.CreateConfiguration(),
            TestHelpers.CreateSigningKeyService(context),
            Mock.Of<UsersServerApi.UsersServerApiClient>(),
            messagesMock.Object,
            Mock.Of<OnlinerServerApi.OnlinerServerApiClient>(),
            context,
            TestHelpers.CreateFederationSwitch(),
            new IncomingPresenceRegistry(),
            TestHelpers.CreatePresenceOptions(),
            new FakeFetchFileRateLimiter(),
            Mock.Of<FilesServerApi.FilesServerApiClient>(),
            new FakeTypingRateLimiter(),
            new TypingValidationCache(TestHelpers.CreatePresenceOptions()),
            new MetricsCollector(),
            quotaLimiter ?? new FakeChatCreatedQuotaLimiter(),
            NullLogger<FederationS2SApiHandler>.Instance);
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

    private static FederationEvent SignedChatCreated(FederationSigningKey key, string origin = Origin)
    {
        var evt = new FederationEvent
        {
            EventId = Guid.NewGuid().ToString(),
            OriginServer = origin,
            OriginTsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ChatCreated = new ChatCreatedPayload
            {
                ChatId = Guid.NewGuid().ToString(),
                Initiator = new FederatedUser { Uuid = Guid.NewGuid().ToString(), ServerName = origin },
                Invitee = new FederatedUser { Uuid = Guid.NewGuid().ToString(), ServerName = TestHelpers.OwnServerName },
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

    // Этап 2.4: MessageEdited/MessageDeleted/MessagesRead маршрутизируются в ApplyFederatedEdit/Delete/Read.
    // Payload'ы этих типов не несут identity автора (P2-02 проверяется локально в Messages) — Federation
    // лишь прокидывает origin (уже сверенный с x-bf-origin) и event_id для LWW tie-break.

    private static FederationEvent Signed(FederationSigningKey key, string origin, Action<FederationEvent> setPayload)
    {
        var evt = new FederationEvent
        {
            EventId = Guid.NewGuid().ToString(),
            OriginServer = origin,
            OriginTsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        setPayload(evt);
        EventSigner.Sign(evt, key);
        return evt;
    }

    private static FederationEvent SignedMessageEdited(FederationSigningKey key, string origin = Origin)
        => Signed(key, origin, evt => evt.MessageEdited = new MessageEditedPayload
        {
            ChatId = Guid.NewGuid().ToString(),
            FederatedMessageId = Guid.NewGuid().ToString(),
            NewText = "edited",
        });

    private static FederationEvent SignedMessageDeleted(FederationSigningKey key, string origin = Origin)
        => Signed(key, origin, evt => evt.MessageDeleted = new MessageDeletedPayload
        {
            ChatId = Guid.NewGuid().ToString(),
            FederatedMessageId = Guid.NewGuid().ToString(),
        });

    private static FederationEvent SignedMessagesRead(FederationSigningKey key, string origin = Origin)
        => Signed(key, origin, evt => evt.MessagesRead = new MessagesReadPayload
        {
            ChatId = Guid.NewGuid().ToString(),
            ReaderUuid = Guid.NewGuid().ToString(),
            UpToFederatedMessageId = Guid.NewGuid().ToString(),
        });

    private static void SetupApplyEdit(Mock<MessagesServerApi.MessagesServerApiClient> mock, ApplyFederatedEditResponse resp)
    {
        mock.Setup(c => c.ApplyFederatedEditAsync(It.IsAny<ApplyFederatedEditRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<ApplyFederatedEditResponse>(
                Task.FromResult(resp), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
    }

    private static void SetupApplyDelete(Mock<MessagesServerApi.MessagesServerApiClient> mock, ApplyFederatedDeleteResponse resp)
    {
        mock.Setup(c => c.ApplyFederatedDeleteAsync(It.IsAny<ApplyFederatedDeleteRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<ApplyFederatedDeleteResponse>(
                Task.FromResult(resp), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
    }

    private static void SetupApplyRead(Mock<MessagesServerApi.MessagesServerApiClient> mock, ApplyFederatedReadResponse resp)
    {
        mock.Setup(c => c.ApplyFederatedReadAsync(It.IsAny<ApplyFederatedReadRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<ApplyFederatedReadResponse>(
                Task.FromResult(resp), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
    }

    [Fact]
    public async Task DeliverEvents_MessageEdited_RoutedToApplyFederatedEditWithOrigin()
    {
        var (context, service, messagesMock) = CreateWithMessagesMock();
        SetupApplyEdit(messagesMock, new ApplyFederatedEditResponse { Applied = true });
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedMessageEdited(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Ok);
        (await context.ProcessedEvents.CountAsync()).Should().Be(1);
        messagesMock.Verify(c => c.ApplyFederatedEditAsync(
            It.Is<ApplyFederatedEditRequest>(r =>
                r.ChatId == evt.MessageEdited.ChatId
                && r.FederatedMessageId == evt.MessageEdited.FederatedMessageId
                && r.NewText == evt.MessageEdited.NewText
                && r.OriginServer == Origin
                && r.EventId == evt.EventId),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeliverEvents_MessageEditedTransient_RetryAndNotIndexed()
    {
        var (context, service, messagesMock) = CreateWithMessagesMock();
        messagesMock
            .Setup(c => c.ApplyFederatedEditAsync(It.IsAny<ApplyFederatedEditRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.NotFound, "message unknown")));
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedMessageEdited(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Retry);
        (await context.ProcessedEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeliverEvents_MessageEditedRejected_RejectedAndIndexed()
    {
        // FailedPrecondition (P2-02 author mismatch и т.п.) — перманентный отказ, но событие всё же
        // считается обработанным (ретраить бессмысленно — источник не изменится).
        var (context, service, messagesMock) = CreateWithMessagesMock();
        messagesMock
            .Setup(c => c.ApplyFederatedEditAsync(It.IsAny<ApplyFederatedEditRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.FailedPrecondition, "author mismatch")));
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedMessageEdited(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Rejected);
    }

    [Fact]
    public async Task DeliverEvents_MessageDeleted_RoutedToApplyFederatedDeleteWithOrigin()
    {
        var (context, service, messagesMock) = CreateWithMessagesMock();
        SetupApplyDelete(messagesMock, new ApplyFederatedDeleteResponse { Applied = true });
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedMessageDeleted(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Ok);
        messagesMock.Verify(c => c.ApplyFederatedDeleteAsync(
            It.Is<ApplyFederatedDeleteRequest>(r =>
                r.ChatId == evt.MessageDeleted.ChatId
                && r.FederatedMessageId == evt.MessageDeleted.FederatedMessageId
                && r.OriginServer == Origin
                && r.EventId == evt.EventId),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeliverEvents_MessagesRead_RoutedToApplyFederatedReadWithOrigin()
    {
        var (context, service, messagesMock) = CreateWithMessagesMock();
        SetupApplyRead(messagesMock, new ApplyFederatedReadResponse { Applied = true });
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedMessagesRead(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Ok);
        messagesMock.Verify(c => c.ApplyFederatedReadAsync(
            It.Is<ApplyFederatedReadRequest>(r =>
                r.ChatId == evt.MessagesRead.ChatId
                && r.ReaderUuid == evt.MessagesRead.ReaderUuid
                && r.UpToFederatedMessageId == evt.MessagesRead.UpToFederatedMessageId
                && r.OriginServer == Origin),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeliverEvents_ChatCreatedQuotaExceeded_RetryAndNotRouted()
    {
        // Этап 2.5: квота per-origin — превышение не должно долетать до Messages, RETRY (не порча события).
        var (context, service, messagesMock) = CreateWithMessagesMock(new FakeChatCreatedQuotaLimiter { AlwaysReject = true });
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedChatCreated(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Retry);
        messagesMock.Verify(c => c.ImportFederatedChatAsync(
            It.IsAny<ImportFederatedChatRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Never);
        (await context.ProcessedEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeliverEvents_ChatCreatedWithinQuota_RoutedToImportFederatedChat()
    {
        var (context, service, messagesMock) = CreateWithMessagesMock(new FakeChatCreatedQuotaLimiter { AlwaysReject = false });
        messagesMock.Setup(c => c.ImportFederatedChatAsync(It.IsAny<ImportFederatedChatRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<ImportFederatedChatResponse>(
                Task.FromResult(new ImportFederatedChatResponse { Imported = true }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedChatCreated(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Ok);
        messagesMock.Verify(c => c.ImportFederatedChatAsync(
            It.IsAny<ImportFederatedChatRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeliverEvents_ChatCreatedPermanentRejectionWithErrorCode_PropagatesErrorCodeToResult()
    {
        // Баг #1: код ошибки permanent-исключения (например FederatedDmRejected) обязан долететь до
        // EventResult.ErrorCode — иначе OutboxDispatcher не сможет опубликовать FederatedChatRejectedEvent.
        var (context, service, messagesMock) = CreateWithMessagesMock(new FakeChatCreatedQuotaLimiter { AlwaysReject = false });
        var trailers = new Metadata { { "x-error-code", "FederatedDmRejected" } };
        messagesMock
            .Setup(c => c.ImportFederatedChatAsync(It.IsAny<ImportFederatedChatRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.FailedPrecondition, "denied"), trailers));

        var key = await SeedOriginKeyAsync(context);
        var evt = SignedChatCreated(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Rejected);
        result.ErrorCode.Should().Be("FederatedDmRejected");
    }

    [Fact]
    public async Task DeliverEvents_UnclassifiedStatusCode_RetryNotThrown()
    {
        // Баг #2: StatusCode.Unknown (например, из общего ServerExceptionInterceptor Messages) не подпадает
        // ни под IsPermanent, ни под IsTransient — должен безопасно деградировать до RETRY, а не падать наружу.
        var (context, service, messagesMock) = CreateWithMessagesMock();
        messagesMock
            .Setup(c => c.ImportFederatedMessageAsync(It.IsAny<ImportFederatedMessageRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unknown, "unexpected")));

        var key = await SeedOriginKeyAsync(context);
        var evt = SignedNewMessage(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Retry);
    }

    [Fact]
    public async Task DeliverEvents_OneEventThrowsUnexpectedException_OtherEventsStillProcessed()
    {
        // Баг #2: необработанное исключение (не RpcException) на одном событии не должно ронять весь батч
        // DeliverEvents — остальные события в том же запросе обрабатываются как обычно.
        var (context, service, messagesMock) = CreateWithMessagesMock(new FakeChatCreatedQuotaLimiter { AlwaysReject = false });
        messagesMock
            .Setup(c => c.ImportFederatedMessageAsync(It.IsAny<ImportFederatedMessageRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("непредвиденная ошибка"));
        messagesMock
            .Setup(c => c.ImportFederatedChatAsync(It.IsAny<ImportFederatedChatRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<ImportFederatedChatResponse>(
                Task.FromResult(new ImportFederatedChatResponse { Imported = true }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var key = await SeedOriginKeyAsync(context);
        var evt1 = SignedNewMessage(key);
        var evt2 = SignedChatCreated(key);

        var request = new DeliverEventsRequest();
        request.Events.Add(evt1);
        request.Events.Add(evt2);
        var response = await service.DeliverEvents(request, TestHelpers.CreateCallContext(Origin));

        response.Results.Should().HaveCount(2);
        response.Results[0].Status.Should().Be(EventStatus.Retry);
        response.Results[1].Status.Should().Be(EventStatus.Ok);
    }

    [Fact]
    public async Task DeliverEvents_NewMessage_IncludesEventId()
    {
        // Баг #7: EventId обязан прокидываться в ImportFederatedMessageRequest — иначе LWW tie-break на
        // приёмнике теряет истинный event_id первого создания сообщения (остаётся Guid.Empty).
        var (context, service, messagesMock) = CreateWithMessagesMock();
        SetupMessagesImport(messagesMock, new ImportFederatedMessageResponse());
        var key = await SeedOriginKeyAsync(context);
        var evt = SignedNewMessage(key);

        var result = await DeliverOneAsync(service, evt);

        result.Status.Should().Be(EventStatus.Ok);
        messagesMock.Verify(c => c.ImportFederatedMessageAsync(
            It.Is<ImportFederatedMessageRequest>(r => r.EventId == evt.EventId),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
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
