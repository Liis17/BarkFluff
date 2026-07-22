using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Messages;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;

using MassTransit;

using MediatR;

namespace BarkFluff.Onliner.Features.SetOnlineStatus;

public class SetOnlineStatusCommandHandler : IRequestHandler<SetOnlineStatusCommand, SetOnlineStatusResponse>
{
    private readonly UserContext _userContext;
    private readonly IPresenceStore _presence;
    private readonly IPublishEndpoint _publish;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SetOnlineStatusCommandHandler> _logger;

    public SetOnlineStatusCommandHandler(
        UserContext userContext,
        IPresenceStore presence,
        IPublishEndpoint publish,
        MetricsCollector metrics,
        ILogger<SetOnlineStatusCommandHandler> logger)
    {
        _userContext = userContext;
        _presence = presence;
        _publish = publish;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<SetOnlineStatusResponse> Handle(
        SetOnlineStatusCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;

        _logger.LogTrace("Setting online status for user {UserId}", userId);

        // Heartbeat в общий presence-стор (Redis). true — переход Offline/absent → Online.
        // Переход Online → Offline обрабатывает OfflineDetectionService (single-runner).
        var becameOnline = await _presence.MarkOnlineAsync(userId, cancellationToken);

        if (becameOnline)
        {
            _metrics.Increment("status_changes.online");

            _logger.LogDebug("User {UserId} status changed to Online, publishing to subscribers", userId);

            // Fan-out: событие доставят подписчикам все инстансы (стрим подписчика может жить на другом).
            await _publish.Publish(new OnlineStatusChangedEvent
            {
                UserId = userId,
                Status = (int)Domain.Enums.StatusTypeId.Online,
                LastSeen = DateTime.UtcNow,
            }, cancellationToken);
        }

        return new SetOnlineStatusResponse();
    }
}
