using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;

using Google.Protobuf;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.Infrastructure;

// Построение подписанного FederationEvent из внутренних событий и вставка в outbox
// (этап 2.2, docs/rearch/04 §3 RabbitMQ). По одной строке outbox на каждую ноду-участника.
//
// События с IsFirstMessageInChat (NewMessageEvent) дают две строки: ChatCreated, затем NewMessage
// (упорядочено по Id). Поля, специфичные для рендера (текст сообщения и т.д.), Messages начнёт
// заполнять в 2.3 — в этом этапе payload строится из расширенных полей, которых достаточно для
// прохождения конвейра (приёмник ответит NotImplementedYet до 2.3).
public class OutboxWriter
{
    private readonly FederationContext _context;
    private readonly SigningKeyService _signingKeyService;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;

    public OutboxWriter(
        FederationContext context,
        SigningKeyService signingKeyService,
        IConfiguration configuration,
        MetricsCollector metrics)
    {
        _context = context;
        _signingKeyService = signingKeyService;
        _configuration = configuration;
        _metrics = metrics;
    }

    public async Task EnqueueSignedAsync(
        FederationEvent evt,
        Guid? chatId,
        IReadOnlyCollection<string> destinations,
        CancellationToken ct = default)
    {
        if (destinations.Count == 0)
            return;

        var key = await _signingKeyService.GetActiveKeyAsync(ct);
        EventSigner.Sign(evt, key);

        var now = DateTime.UtcNow;
        var payloadBytes = evt.ToByteArray();
        var eventType = evt.PayloadCase.ToString();
        var ownServerName = _configuration["Federation:ServerName"] ?? string.Empty;

        foreach (var destination in destinations.Distinct())
        {
            if (string.Equals(destination, ownServerName, StringComparison.OrdinalIgnoreCase))
                continue; // не отправляем самим себе (свой ServerName в RemoteParticipants — артефакт).

            _context.Outbox.Add(new FederationOutbox
            {
                Destination = destination,
                ChatId = chatId,
                EventId = Guid.Parse(evt.EventId),
                EventType = eventType,
                PayloadBytes = payloadBytes,
                CreatedAt = now,
                Attempts = 0,
                NextAttemptAt = now,
                Status = OutboxStatus.Pending,
            });
        }

        await _context.SaveChangesAsync(ct);
        _metrics.Add("outbox_enqueued_total", destinations.Count);
    }
}
