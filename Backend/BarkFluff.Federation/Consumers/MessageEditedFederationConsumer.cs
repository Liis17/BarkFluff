using BarkFluff.Federation.Infrastructure;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

using Microsoft.Extensions.Configuration;

namespace BarkFluff.Federation.Consumers;

public class MessageEditedFederationConsumer : IConsumer<MessageEditedEvent>
{
    private readonly OutboxWriter _writer;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;

    public MessageEditedFederationConsumer(
        OutboxWriter writer,
        IConfiguration configuration,
        MetricsCollector metrics)
    {
        _writer = writer;
        _configuration = configuration;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<MessageEditedEvent> context)
    {
        var msg = context.Message;
        if (!IsFederationEnabled() || !msg.IsFederated || msg.RemoteParticipants.Count == 0)
            return;

        var destinations = msg.RemoteParticipants.Select(p => p.ServerName).ToList();
        var origin = _configuration["Federation:ServerName"] ?? string.Empty;
        var ts = (msg.LastChangeAt ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();

        var evt = new FederationEvent
        {
            EventId = Guid.NewGuid().ToString(),
            OriginServer = origin,
            OriginTsMs = ts,
        };
        evt.MessageEdited = new MessageEditedPayload
        {
            ChatId = msg.ChatId.ToString(),
            FederatedMessageId = (msg.FederatedId ?? Guid.NewGuid()).ToString(),
            NewText = string.Empty, // текст не извлекается из byte[] Message до 2.3.
        };

        await _writer.EnqueueSignedAsync(evt, msg.ChatId, destinations, context.CancellationToken);
        _metrics.Increment("federation_consumer_message_edited");
    }

    private bool IsFederationEnabled()
        => string.Equals(_configuration["Federation:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
}
