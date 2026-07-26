using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Onliner.Messages;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;

using MassTransit;

using MediatR;

namespace BarkFluff.Onliner.Features.UpsertRemoteStatus;

/// <summary>
/// Federation вливает статус remote-пользователя (этап 4.2): пишем в TTL-кеш и разносим
/// fan-out'ом — стрим подписчика может жить на другом инстансе.
/// Право вызывающего здесь не проверяется: это Federation своей ноды с service-токеном,
/// а origin/членство проверены до вызова (этапы 4.3/4.4).
/// </summary>
public class UpsertRemoteStatusCommandHandler
    : IRequestHandler<UpsertRemoteStatusCommand, UpsertRemoteStatusResponse>
{
    private readonly IRemotePresenceStore _remotePresence;
    private readonly IPublishEndpoint _publish;
    private readonly MetricsCollector _metrics;

    public UpsertRemoteStatusCommandHandler(
        IRemotePresenceStore remotePresence,
        IPublishEndpoint publish,
        MetricsCollector metrics)
    {
        _remotePresence = remotePresence;
        _publish = publish;
        _metrics = metrics;
    }

    public async Task<UpsertRemoteStatusResponse> Handle(
        UpsertRemoteStatusCommand request,
        CancellationToken cancellationToken)
    {
        _metrics.Increment("remote_status_upserts");

        await _remotePresence.UpsertAsync(
            request.UserUuid, request.Status, request.LastSeen, cancellationToken);

        await _publish.Publish(new OnlineStatusChangedEvent
        {
            UserId = 0,
            UserUuid = request.UserUuid,
            Status = (int)request.Status,
            LastSeen = request.LastSeen,
        }, cancellationToken);

        return new UpsertRemoteStatusResponse();
    }
}
