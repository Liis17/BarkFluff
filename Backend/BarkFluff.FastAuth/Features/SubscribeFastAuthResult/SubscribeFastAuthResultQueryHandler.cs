using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Exceptions.FastAuth;

namespace BarkFluff.FastAuth.Features.SubscribeFastAuthResult;

public class SubscribeFastAuthResultQueryHandler(
    FastAuthSessionsManager sessions,
    MetricsCollector metrics,
    ILogger<SubscribeFastAuthResultQueryHandler> logger)
{
    public async Task Handle(SubscribeFastAuthResultQuery request)
    {
        var session = sessions.TryGet(request.FastAuthId)
            ?? throw new FastAuthSessionNotFoundException();

        if (!session.TryAttachSubscriber())
        {
            throw new FastAuthInvalidStateException();
        }

        metrics.Increment("active_subscriptions");
        logger.LogInformation("FastAuth subscription attached to session {Id}", session.Id);

        try
        {
            await foreach (var evt in session.Events.ReadAllAsync(request.CancellationToken))
            {
                await request.ResponseStream.WriteAsync(evt, request.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("FastAuth subscription on session {Id} cancelled by client", session.Id);
        }
        finally
        {
            metrics.Increment("active_subscriptions_closed");
        }
    }
}
