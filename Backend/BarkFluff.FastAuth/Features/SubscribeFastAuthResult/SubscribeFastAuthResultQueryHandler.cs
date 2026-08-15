using System.Threading.Channels;

using BarkFluff.FastAuth.Domain;
using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Exceptions.FastAuth;

namespace BarkFluff.FastAuth.Features.SubscribeFastAuthResult;

public class SubscribeFastAuthResultQueryHandler(
    IFastAuthSessionStore sessions,
    IFastAuthEventBus eventBus,
    MetricsCollector metrics,
    ILogger<SubscribeFastAuthResultQueryHandler> logger)
{
    public async Task Handle(SubscribeFastAuthResultQuery request)
    {
        var ct = request.CancellationToken;

        var session = await sessions.GetAsync(request.FastAuthId, ct)
            ?? throw new FastAuthSessionNotFoundException();

        // Уже финализирована (в окне FinalRetention) — отдаём результат сразу,
        // это же путь реконнекта после Accept.
        if (session.IsFinal)
        {
            await WriteAsync(request, session.Result?.ToProto() ?? new FastAuthResult { Status = session.Status });
            return;
        }

        // Логически истекла, но значение ещё в Redis — финализируем и закрываем стрим.
        if (DateTime.UtcNow >= session.ExpiresAt)
        {
            if (await sessions.TryExpireAsync(request.FastAuthId, ct))
            {
                metrics.Increment("sessions_expired");
            }

            await WriteAsync(request, new FastAuthResult { Status = FastAuthStatus.Expired });
            return;
        }

        // Единственный подписчик на сессию — глобально, на все инстансы.
        var lockTtl = session.ExpiresAt - DateTime.UtcNow + FastAuthSessionTiming.FinalRetention;
        var ownerToken = await sessions.TryAttachSubscriberAsync(request.FastAuthId, lockTtl, ct);
        if (ownerToken is null)
        {
            throw new FastAuthInvalidStateException();
        }

        metrics.Increment("active_subscriptions");
        logger.LogInformation("FastAuth subscription attached to session {Id}", session.Id[..8]);

        try
        {
            await StreamEventsAsync(request, session);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("FastAuth subscription on session {Id} cancelled by client", session.Id[..8]);
        }
        finally
        {
            metrics.Increment("active_subscriptions_closed");
            eventBus.Detach(request.FastAuthId);
            await sessions.ReleaseSubscriberAsync(request.FastAuthId, ownerToken, CancellationToken.None);
        }
    }

    private async Task StreamEventsAsync(SubscribeFastAuthResultQuery request, FastAuthSessionState session)
    {
        var reader = eventBus.Attach(request.FastAuthId);
        if (reader is null)
        {
            throw new FastAuthInvalidStateException();
        }

        // Перечитываем ПОСЛЕ локальной подписки: переход, случившийся до неё,
        // виден финальным состоянием в сторе — событие не теряется.
        var latest = await sessions.GetAsync(request.FastAuthId, request.CancellationToken);

        if (latest is { IsFinal: true })
        {
            await WriteAsync(request, latest.Result?.ToProto() ?? new FastAuthResult { Status = latest.Status });
            return;
        }

        if (latest is null)
        {
            await WriteAsync(request, new FastAuthResult { Status = FastAuthStatus.Expired });
            return;
        }

        var remaining = latest.ExpiresAt - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            if (await sessions.TryExpireAsync(request.FastAuthId, CancellationToken.None))
            {
                metrics.Increment("sessions_expired");
            }

            await WriteAsync(request, new FastAuthResult { Status = FastAuthStatus.Expired });
            return;
        }

        // Локальный дедлайн до ExpiresAt вместо sweeper'а: TTL Redis чистит данные,
        // а клиенту стрим закрываем сами.
        using var deadlineCts = new CancellationTokenSource(remaining);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            request.CancellationToken, deadlineCts.Token);

        try
        {
            await StreamChannelAsync(request, reader, linked.Token);
        }
        catch (OperationCanceledException) when (deadlineCts.IsCancellationRequested
                                                 && !request.CancellationToken.IsCancellationRequested)
        {
            if (await sessions.TryExpireAsync(request.FastAuthId, CancellationToken.None))
            {
                metrics.Increment("sessions_expired");
            }

            await WriteAsync(request, new FastAuthResult { Status = FastAuthStatus.Expired });
        }
    }

    private static async Task StreamChannelAsync(
        SubscribeFastAuthResultQuery request,
        ChannelReader<FastAuthResult> reader,
        CancellationToken linkedToken)
    {
        await foreach (var evt in reader.ReadAllAsync(linkedToken))
        {
            await request.ResponseStream.WriteAsync(evt, request.CancellationToken);
        }
    }

    private static Task WriteAsync(SubscribeFastAuthResultQuery request, FastAuthResult result)
    {
        return request.ResponseStream.WriteAsync(result, request.CancellationToken);
    }
}
