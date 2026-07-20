using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Infrastructure;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Federation.Tests.Infrastructure;

public class OutboxWriterTests
{
    private static (FederationContext Context, OutboxWriter Writer) CreateWriter(IConfiguration? configuration = null)
    {
        var context = TestHelpers.CreateContext();
        var writer = new OutboxWriter(
            context,
            TestHelpers.CreateSigningKeyService(context),
            configuration ?? TestHelpers.CreateConfiguration(),
            new MetricsCollector());
        return (context, writer);
    }

    private static FederationEvent NewMessageEvent(string? eventId = null, string origin = TestHelpers.OwnServerName)
        => new()
        {
            EventId = (eventId ?? Guid.NewGuid().ToString()),
            OriginServer = origin,
            OriginTsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            NewMessage = new NewMessagePayload
            {
                ChatId = Guid.NewGuid().ToString(),
                FederatedMessageId = Guid.NewGuid().ToString(),
                Sender = new FederatedUser { Uuid = Guid.NewGuid().ToString(), ServerName = origin },
            },
        };

    [Fact]
    public async Task EnqueueSignedAsync_NoDestinations_DoesNothing()
    {
        var (context, writer) = CreateWriter();
        await TestHelpers.EnsureActiveKeyAsync(context);

        await writer.EnqueueSignedAsync(NewMessageEvent(), Guid.NewGuid(), []);

        (await context.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task EnqueueSignedAsync_OneRowPerDestination_OwnServerExcluded()
    {
        var (context, writer) = CreateWriter();
        var key = await TestHelpers.EnsureActiveKeyAsync(context);
        var chatId = Guid.NewGuid();
        var evt = NewMessageEvent();

        await writer.EnqueueSignedAsync(evt, chatId, ["peer-a.test", "peer-b.test", TestHelpers.OwnServerName, "peer-a.test"]);

        var rows = await context.Outbox.OrderBy(r => r.Destination).ToListAsync();
        rows.Should().HaveCount(2); // own-нода и дубликат отфильтрованы
        rows.Select(r => r.Destination).Should().Equal("peer-a.test", "peer-b.test");

        foreach (var row in rows)
        {
            row.ChatId.Should().Be(chatId);
            row.EventId.Should().Be(Guid.Parse(evt.EventId));
            row.EventType.Should().Be(nameof(FederationEvent.PayloadOneofCase.NewMessage));
            row.Status.Should().Be(OutboxStatus.Pending);
            row.Attempts.Should().Be(0);
            row.NextAttemptAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));

            // Payload — подписанный wire-formат: подпись проверяется публичным ключом ноды.
            var parsed = FederationEvent.Parser.ParseFrom(row.PayloadBytes);
            parsed.OriginKeyId.Should().Be(key.KeyId);
            EventSigner.Verify(parsed, key.PublicKey).Should().BeTrue();
        }
    }

    [Fact]
    public async Task EnqueueSignedAsync_OwnServerNameCaseInsensitive()
    {
        var (context, writer) = CreateWriter();
        await TestHelpers.EnsureActiveKeyAsync(context);

        await writer.EnqueueSignedAsync(NewMessageEvent(), null, ["NODE-A.TEST"]);

        (await context.Outbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task EnqueueSignedAsync_SignsEventWithActiveKey()
    {
        var (context, writer) = CreateWriter();
        var key = await TestHelpers.EnsureActiveKeyAsync(context);
        var evt = NewMessageEvent();

        evt.OriginKeyId.Should().BeEmpty();
        evt.OriginSignature.IsEmpty.Should().BeTrue();

        await writer.EnqueueSignedAsync(evt, null, ["peer.test"]);

        // Переданный объект мутируется подписью (контракт OutboxWriter/EventSigner).
        evt.OriginKeyId.Should().Be(key.KeyId);
        evt.OriginSignature.IsEmpty.Should().BeFalse();
    }
}
