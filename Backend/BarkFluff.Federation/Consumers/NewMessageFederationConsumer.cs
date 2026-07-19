using BarkFluff.Federation.Infrastructure;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Shared.Queue.Federation;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

using Microsoft.Extensions.Configuration;

namespace BarkFluff.Federation.Consumers;

// Консюмер NewMessageEvent → FederationOutbox.
// Игнорирует нефедеративные сообщения (IsFederated=false) и выключенную федерацию.
// Для IsFirstMessageInChat=true вставляет две строки: ChatCreated, затем NewMessage.
public class NewMessageFederationConsumer : IConsumer<NewMessageEvent>
{
    private readonly OutboxWriter _writer;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;

    public NewMessageFederationConsumer(
        OutboxWriter writer,
        IConfiguration configuration,
        MetricsCollector metrics)
    {
        _writer = writer;
        _configuration = configuration;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<NewMessageEvent> context)
    {
        var msg = context.Message;

        if (!IsFederationEnabled() || !msg.IsFederated || msg.RemoteParticipants.Count == 0)
            return;

        var ct = context.CancellationToken;
        var destinations = msg.RemoteParticipants.Select(p => p.ServerName).ToList();
        var origin = _configuration["Federation:ServerName"] ?? string.Empty;
        var ts = (msg.LastChangeAt ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();

        if (msg.IsFirstMessageInChat
            && msg.InitiatorUuid.HasValue
            && msg.InviteeUuid.HasValue
            && msg.FederatedId.HasValue)
        {
            // ChatCreated предшествует NewMessage. initiator — наш пользователь (origin нода),
            // invitee — remote (на destination), но оба поля отдаём одинаково для обеих сторон.
            var chatCreated = BuildEvent(Guid.NewGuid(), origin, ts, evt => evt.ChatCreated = new ChatCreatedPayload
            {
                ChatId = msg.ChatId.ToString(),
                Initiator = new FederatedUser
                {
                    Uuid = msg.InitiatorUuid.Value.ToString(),
                    Username = msg.SenderFid ?? string.Empty,
                    ServerName = origin,
                },
                Invitee = new FederatedUser
                {
                    Uuid = msg.InviteeUuid.Value.ToString(),
                    ServerName = destinations.First(),
                },
            });
            await _writer.EnqueueSignedAsync(chatCreated, msg.ChatId, destinations, ct);
        }

        // event_id — новый uuid (spec 2.2 §Изменение 4, как ChatCreated и остальные консюмеры);
        // federated_message_id — стабильный id сообщения, вычисляется один раз (P2-01).
        var federatedMessageId = msg.FederatedId ?? Guid.NewGuid();
        var newMessage = BuildEvent(
            Guid.NewGuid(),
            origin,
            ts,
            evt => evt.NewMessage = new NewMessagePayload
            {
                ChatId = msg.ChatId.ToString(),
                FederatedMessageId = federatedMessageId.ToString(),
                Sender = new FederatedUser
                {
                    Uuid = (msg.SenderUuid ?? Guid.Empty).ToString(),
                    ServerName = origin,
                },
                // Текст не извлекается из byte[] Message в этом этапе — Messages будет передавать
                // готовое представление в 2.3. Для прохождения конвейра (RETRY:NotImplementedYet)
                // текст не нужен.
                Text = string.Empty,
            });
        await _writer.EnqueueSignedAsync(newMessage, msg.ChatId, destinations, ct);

        _metrics.Increment("federation_consumer_new_message");
    }

    private bool IsFederationEnabled()
        => string.Equals(_configuration["Federation:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

    private static FederationEvent BuildEvent(Guid eventId, string origin, long tsMs, Action<FederationEvent> payloadSetter)
    {
        var evt = new FederationEvent
        {
            EventId = eventId.ToString(),
            OriginServer = origin,
            OriginTsMs = tsMs,
        };
        payloadSetter(evt);
        return evt;
    }
}
