using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Queue.Federation;

using MassTransit;

namespace BarkFluff.Federation.Consumers;

/// <summary>
/// Fan-out ротации ключа подписи XFed: каждый инстанс перезагружает локальные кэши — иначе после
/// ротации на одном инстансе остальные продолжают подписывать исходящие S2S-запросы старым ключом
/// до рестарта (масштабирование, docs/scaling/federation.md).
/// </summary>
public class SigningKeyRotatedConsumer(
    ActiveSigningKeyCache activeKeyCache,
    WellKnownDocumentService wellKnownDocumentService,
    MetricsCollector metrics,
    ILogger<SigningKeyRotatedConsumer> logger)
    : IConsumer<SigningKeyRotatedEvent>
{
    public async Task Consume(ConsumeContext<SigningKeyRotatedEvent> context)
    {
        var msg = context.Message;
        metrics.Increment("signing_key_rotated_received");
        logger.LogInformation("Получено событие ротации ключа подписи: NewKeyId={NewKeyId}", msg.NewKeyId);

        await activeKeyCache.RefreshAsync(context.CancellationToken);
        await wellKnownDocumentService.RebuildAsync(context.CancellationToken);
        metrics.Set("last_signing_key_rotation_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
}
