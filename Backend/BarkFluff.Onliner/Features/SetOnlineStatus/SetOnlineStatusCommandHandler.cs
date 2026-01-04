using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Onliner.Features.SetOnlineStatus;

public class SetOnlineStatusCommandHandler : IRequestHandler<SetOnlineStatusCommand, SetOnlineStatusResponse>
{
    private readonly UserContext _userContext;
    private readonly OnlineStatusStorage _storage;
    private readonly OnlineStatusNotifier _notifier;
    private readonly ILogger<SetOnlineStatusCommandHandler> _logger;

    public SetOnlineStatusCommandHandler(
        UserContext userContext,
        OnlineStatusStorage storage,
        OnlineStatusNotifier notifier,
        ILogger<SetOnlineStatusCommandHandler> logger)
    {
        _userContext = userContext;
        _storage = storage;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<SetOnlineStatusResponse> Handle(
        SetOnlineStatusCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;

        _logger.LogTrace("Setting online status for user {UserId}", userId);

        // Обновляем статус в in-memory storage
        bool statusChanged = _storage.UpdateStatus(userId);

        // Если статус изменился - уведомляем подписчиков
        // Переход Offline → Online обрабатывается здесь
        // Переход Online → Offline обрабатывается в OfflineDetectionService
        if (statusChanged)
        {
            var currentStatus = _storage.GetStatus(userId);

            if (currentStatus != null)
            {
                _logger.LogDebug("User {UserId} status changed to {Status}, notifying subscribers",
                    userId, currentStatus.Status);
                await _notifier.NotifyStatusChanged(userId, currentStatus, cancellationToken);
            }
        }

        return new SetOnlineStatusResponse();
    }
}