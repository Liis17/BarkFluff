using BarkFluff.Federation.BackgroundServices;
using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.Proto.Federation;
using BarkFluff.Shared.Queue.Federation;

using Google.Protobuf;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BarkFluff.Federation.Tests.BackgroundServices;

public class OutboxDispatcherTests
{
    private static async Task<(TestHelpers.TestDatabase Db, ServiceProvider Provider, OutboxDispatcher Dispatcher)> CreateDispatcherAsync(
        IDictionary<string, string?>? configOverrides = null,
        IPublishEndpoint? publishEndpoint = null)
    {
        var db = TestHelpers.CreateDatabase();
        var configuration = TestHelpers.CreateConfiguration(configOverrides);
        var provider = TestHelpers.CreateProvider(db, configuration, publishEndpoint: publishEndpoint);

        // Активный ключ нужен XFedClientInterceptor'у при реальных исходящих вызовах.
        await using (var seed = TestHelpers.CreateContext(db))
        {
            await TestHelpers.EnsureActiveKeyAsync(seed);
        }
        await provider.GetRequiredService<ActiveSigningKeyCache>().RefreshAsync();

        var dispatcher = new OutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            provider.GetRequiredService<FederationSwitch>(),
            provider.GetRequiredService<BarkFluff.GrpcServer.Metrics.MetricsCollector>(),
            NullLogger<OutboxDispatcher>.Instance);

        return (db, provider, dispatcher);
    }

    private static byte[] EventPayload(Guid eventId)
        => new FederationEvent
        {
            EventId = eventId.ToString(),
            OriginServer = TestHelpers.OwnServerName,
            OriginTsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            NewMessage = new NewMessagePayload
            {
                ChatId = Guid.NewGuid().ToString(),
                FederatedMessageId = Guid.NewGuid().ToString(),
            },
        }.ToByteArray();

    private static async Task<FederationOutbox> SeedRowAsync(
        TestHelpers.TestDatabase db,
        string destination,
        Guid? chatId = null,
        byte[]? payload = null,
        Guid? eventId = null)
    {
        await using var context = TestHelpers.CreateContext(db);
        var id = eventId ?? Guid.NewGuid();
        var row = new FederationOutbox
        {
            Destination = destination,
            ChatId = chatId,
            EventId = id,
            EventType = "NewMessage",
            PayloadBytes = payload ?? EventPayload(id),
            CreatedAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending,
        };
        context.Outbox.Add(row);
        await context.SaveChangesAsync();
        return row;
    }

    private static async Task SeedManualPeerAsync(TestHelpers.TestDatabase db, string serverName, string endpoint)
    {
        await using var context = TestHelpers.CreateContext(db);
        context.KnownServers.Add(new KnownServer
        {
            ServerName = serverName,
            FederationEndpoint = endpoint,
            Source = KnownServerSource.Manual,
            Status = KnownServerStatus.Active,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            ProtocolVersion = 1,
        });
        await context.SaveChangesAsync();
    }

    private static async Task<FederationOutbox> ReloadAsync(TestHelpers.TestDatabase db, long id)
    {
        await using var context = TestHelpers.CreateContext(db);
        return await context.Outbox.SingleAsync(r => r.Id == id);
    }

    [Fact]
    public async Task Dispatch_InactiveSwitch_RowsUntouched()
    {
        var (db, provider, dispatcher) = await CreateDispatcherAsync(new Dictionary<string, string?>
        {
            ["Federation:Enabled"] = "false",
        });
        using (provider)
        {
            var row = await SeedRowAsync(db, "ghost.test");

            await dispatcher.StartAsync(CancellationToken.None);
            await Task.Delay(300); // негативный кейс: ждём фиксированно, эффекта быть не должно
            await dispatcher.StopAsync(CancellationToken.None);

            var reloaded = await ReloadAsync(db, row.Id);
            reloaded.Attempts.Should().Be(0);
            reloaded.Status.Should().Be(OutboxStatus.Pending);
        }
    }

    [Fact]
    public async Task Dispatch_UnresolvedPeer_BackoffWithReason()
    {
        var (db, provider, dispatcher) = await CreateDispatcherAsync();
        using (provider)
        {
            var row = await SeedRowAsync(db, "ghost.test");
            var before = DateTime.UtcNow;

            await dispatcher.StartAsync(CancellationToken.None);
            await TestHelpers.WaitUntilAsync(
                async () => (await ReloadAsync(db, row.Id)).Attempts == 1,
                "ожидалась попытка доставки с backoff");
            await dispatcher.StopAsync(CancellationToken.None);

            var reloaded = await ReloadAsync(db, row.Id);
            reloaded.Status.Should().Be(OutboxStatus.Pending);
            reloaded.LastError.Should().Be("peer_unresolved");
            // Первая ступень backoff — 30 секунд.
            reloaded.NextAttemptAt.Should().BeOnOrAfter(before + TimeSpan.FromSeconds(29));
            reloaded.NextAttemptAt.Should().BeOnOrBefore(DateTime.UtcNow + TimeSpan.FromSeconds(35));
        }
    }

    [Fact]
    public async Task Dispatch_MaxAttemptsReached_GoesToDeadLetter()
    {
        var (db, provider, dispatcher) = await CreateDispatcherAsync(new Dictionary<string, string?>
        {
            ["Federation:OutboxMaxAttempts"] = "1",
        });
        using (provider)
        {
            var row = await SeedRowAsync(db, "ghost.test");

            await dispatcher.StartAsync(CancellationToken.None);
            await TestHelpers.WaitUntilAsync(
                async () => (await ReloadAsync(db, row.Id)).Status == OutboxStatus.DeadLetter,
                "строка должна уйти в DeadLetter после исчерпания попыток");
            await dispatcher.StopAsync(CancellationToken.None);

            var reloaded = await ReloadAsync(db, row.Id);
            reloaded.LastError.Should().Be("max_attempts:peer_unresolved");
        }
    }

    [Fact]
    public async Task Dispatch_PerChatOrdering_OnlyHeadOfEachChatIsDispatched()
    {
        var (db, provider, dispatcher) = await CreateDispatcherAsync();
        using (provider)
        {
            var chatX = Guid.NewGuid();
            var first = await SeedRowAsync(db, "ghost.test", chatX);
            var blocked = await SeedRowAsync(db, "ghost.test", chatX); // ждёт first
            var otherChat = await SeedRowAsync(db, "ghost.test", Guid.NewGuid());
            var noChat = await SeedRowAsync(db, "ghost.test");

            await dispatcher.StartAsync(CancellationToken.None);
            await TestHelpers.WaitUntilAsync(
                async () => (await ReloadAsync(db, first.Id)).Attempts == 1,
                "голова очереди чата должна быть обработана");
            await dispatcher.StopAsync(CancellationToken.None);

            (await ReloadAsync(db, blocked.Id)).Attempts.Should().Be(0, "более позднее событие того же чата ждёт");
            (await ReloadAsync(db, otherChat.Id)).Attempts.Should().Be(1, "чужие чаты не блокируются");
            (await ReloadAsync(db, noChat.Id)).Attempts.Should().Be(1, "события без чата едут без ограничений");
        }
    }

    [Fact]
    public async Task Dispatch_BatchSizeLimit_AtMost100PerDestination()
    {
        var (db, provider, dispatcher) = await CreateDispatcherAsync();
        using (provider)
        {
            var ids = new List<long>();
            for (var i = 0; i < 101; i++)
                ids.Add((await SeedRowAsync(db, "ghost.test")).Id);

            await dispatcher.StartAsync(CancellationToken.None);
            await TestHelpers.WaitUntilAsync(
                async () =>
                {
                    await using var context = TestHelpers.CreateContext(db);
                    return await context.Outbox.CountAsync(r => r.Attempts == 1) == 100;
                },
                "батч ограничен 100 событиями");
            await dispatcher.StopAsync(CancellationToken.None);

            await using var verify = TestHelpers.CreateContext(db);
            (await verify.Outbox.CountAsync(r => r.Attempts == 0)).Should().Be(1);
        }
    }

    [Fact]
    public async Task Dispatch_BatchBytesLimit_StopsAt1Mb()
    {
        var (db, provider, dispatcher) = await CreateDispatcherAsync();
        using (provider)
        {
            var payload = new byte[600_000];
            var first = await SeedRowAsync(db, "ghost.test", payload: payload);
            var second = await SeedRowAsync(db, "ghost.test", payload: payload);

            await dispatcher.StartAsync(CancellationToken.None);
            await TestHelpers.WaitUntilAsync(
                async () => (await ReloadAsync(db, first.Id)).Attempts == 1,
                "первая строка должна попасть в батч");
            await dispatcher.StopAsync(CancellationToken.None);

            (await ReloadAsync(db, second.Id)).Attempts.Should().Be(0, "600КБ + 600КБ не влезают в 1МБ батч");
        }
    }

    [Fact]
    public async Task Dispatch_TransportError_BackoffsWholeBatch()
    {
        var (db, provider, dispatcher) = await CreateDispatcherAsync();
        using (provider)
        {
            // Пир известен, но порт мёртв — соединение отклоняется → RpcException → backoff.
            await SeedManualPeerAsync(db, "peer-down.test", "http://peer-down.test:1");
            var row = await SeedRowAsync(db, "peer-down.test");

            await dispatcher.StartAsync(CancellationToken.None);
            await TestHelpers.WaitUntilAsync(
                async () => (await ReloadAsync(db, row.Id)).Attempts == 1,
                "транспортная ошибка должна дать backoff");
            await dispatcher.StopAsync(CancellationToken.None);

            var reloaded = await ReloadAsync(db, row.Id);
            reloaded.Status.Should().Be(OutboxStatus.Pending);
            reloaded.LastError.Should().Be("transport_error");
        }
    }

    [Fact]
    public async Task Dispatch_SuccessfulDelivery_PerEventClassification()
    {
        var stub = new StubS2SApi();
        await using var server = await LoopbackS2SServer.StartAsync(stub);

        var (db, provider, dispatcher) = await CreateDispatcherAsync();
        using (provider)
        {
            await SeedManualPeerAsync(db, server.HostName, server.Endpoint);

            var ok = await SeedRowAsync(db, server.HostName);
            var duplicate = await SeedRowAsync(db, server.HostName);
            var rejected = await SeedRowAsync(db, server.HostName);
            var retry = await SeedRowAsync(db, server.HostName);
            var noResult = await SeedRowAsync(db, server.HostName);

            stub.OnDeliverEvents = request =>
            {
                var response = new DeliverEventsResponse();
                foreach (var evt in request.Events)
                {
                    if (evt.EventId == ok.EventId.ToString())
                        response.Results.Add(new EventResult { EventId = evt.EventId, Status = EventStatus.Ok });
                    else if (evt.EventId == duplicate.EventId.ToString())
                        response.Results.Add(new EventResult { EventId = evt.EventId, Status = EventStatus.AlreadyProcessed });
                    else if (evt.EventId == rejected.EventId.ToString())
                        response.Results.Add(new EventResult { EventId = evt.EventId, Status = EventStatus.Rejected, ErrorCode = "FederatedDmRejected" });
                    else if (evt.EventId == retry.EventId.ToString())
                        response.Results.Add(new EventResult { EventId = evt.EventId, Status = EventStatus.Retry, ErrorCode = "NotImplementedYet" });
                    // noResult намеренно не добавлен — недостающий результат трактуется как RETRY.
                }
                return response;
            };

            await dispatcher.StartAsync(CancellationToken.None);
            await TestHelpers.WaitUntilAsync(
                async () => (await ReloadAsync(db, ok.Id)).Status == OutboxStatus.Delivered,
                "OK-событие должно быть доставлено");
            await dispatcher.StopAsync(CancellationToken.None);

            (await ReloadAsync(db, ok.Id)).Status.Should().Be(OutboxStatus.Delivered);
            (await ReloadAsync(db, duplicate.Id)).Status.Should().Be(OutboxStatus.Delivered, "ALREADY_PROCESSED засчитывается как доставка");

            var rejectedRow = await ReloadAsync(db, rejected.Id);
            rejectedRow.Status.Should().Be(OutboxStatus.DeadLetter, "REJECTED → DeadLetter немедленно");
            rejectedRow.LastError.Should().Be("FederatedDmRejected");
            rejectedRow.Attempts.Should().Be(0);

            var retryRow = await ReloadAsync(db, retry.Id);
            retryRow.Status.Should().Be(OutboxStatus.Pending);
            retryRow.Attempts.Should().Be(1);
            retryRow.LastError.Should().Be("NotImplementedYet");

            var noResultRow = await ReloadAsync(db, noResult.Id);
            noResultRow.Attempts.Should().Be(1);
            noResultRow.LastError.Should().Be("no_result");
        }
    }

    [Fact]
    public async Task Dispatch_FederatedDmRejected_PublishesFederatedChatRejectedEvent()
    {
        // Этап 2.5: privacy-отказ ChatCreated (DenyFederatedDm) на DeadLetter → origin-нода узнаёт
        // через FederatedChatRejectedEvent и помечает свою копию чата Rejected.
        var stub = new StubS2SApi();
        await using var server = await LoopbackS2SServer.StartAsync(stub);
        var publishMock = new Mock<IPublishEndpoint>();

        var (db, provider, dispatcher) = await CreateDispatcherAsync(publishEndpoint: publishMock.Object);
        using (provider)
        {
            await SeedManualPeerAsync(db, server.HostName, server.Endpoint);
            var chatId = Guid.NewGuid();
            var rejected = await SeedRowAsync(db, server.HostName, chatId);

            stub.OnDeliverEvents = request => new DeliverEventsResponse
            {
                Results =
                {
                    new EventResult { EventId = rejected.EventId.ToString(), Status = EventStatus.Rejected, ErrorCode = "FederatedDmRejected" },
                },
            };

            await dispatcher.StartAsync(CancellationToken.None);
            await TestHelpers.WaitUntilAsync(
                async () => (await ReloadAsync(db, rejected.Id)).Status == OutboxStatus.DeadLetter,
                "строка должна уйти в DeadLetter");
            await dispatcher.StopAsync(CancellationToken.None);

            publishMock.Verify(p => p.Publish(
                It.Is<FederatedChatRejectedEvent>(e => e.ChatId == chatId && e.Reason == "FederatedDmRejected"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task Dispatch_RejectedWithoutChatId_DoesNotPublishFederatedChatRejectedEvent()
    {
        // Вне-чатовые события (ChatId=null, напр. профильные 2.9) не порождают FederatedChatRejectedEvent.
        var stub = new StubS2SApi();
        await using var server = await LoopbackS2SServer.StartAsync(stub);
        var publishMock = new Mock<IPublishEndpoint>();

        var (db, provider, dispatcher) = await CreateDispatcherAsync(publishEndpoint: publishMock.Object);
        using (provider)
        {
            await SeedManualPeerAsync(db, server.HostName, server.Endpoint);
            var rejected = await SeedRowAsync(db, server.HostName, chatId: null);

            stub.OnDeliverEvents = request => new DeliverEventsResponse
            {
                Results =
                {
                    new EventResult { EventId = rejected.EventId.ToString(), Status = EventStatus.Rejected, ErrorCode = "FederatedDmRejected" },
                },
            };

            await dispatcher.StartAsync(CancellationToken.None);
            await TestHelpers.WaitUntilAsync(
                async () => (await ReloadAsync(db, rejected.Id)).Status == OutboxStatus.DeadLetter,
                "строка должна уйти в DeadLetter");
            await dispatcher.StopAsync(CancellationToken.None);

            publishMock.Verify(p => p.Publish(
                It.IsAny<FederatedChatRejectedEvent>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
