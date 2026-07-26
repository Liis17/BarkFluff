using BarkFluff.Federation.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

using Microsoft.Extensions.Configuration;

namespace BarkFluff.Federation.Consumers;

public class MessageReadFederationConsumer : IConsumer<MessageReadEvent>
{
    private readonly OutboxWriter _writer;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;

    public MessageReadFederationConsumer(
        OutboxWriter writer,
        IConfiguration configuration,
        MetricsCollector metrics)
    {
        _writer = writer;
        _configuration = configuration;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<MessageReadEvent> context)
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
        evt.MessagesRead = new MessagesReadPayload
        {
            ChatId = msg.ChatId.ToString(),
            ReaderUuid = (msg.ReaderUuid ?? Guid.Empty).ToString(),
            UpToFederatedMessageId = (msg.UpToFederatedMessageId ?? Guid.Empty).ToString(),
        };

        await _writer.EnqueueSignedAsync(evt, msg.ChatId, destinations, context.CancellationToken);
        _metrics.Increment("federation_consumer_message_read");
    }

    private bool IsFederationEnabled()
        => string.Equals(_configuration["Federation:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
}
