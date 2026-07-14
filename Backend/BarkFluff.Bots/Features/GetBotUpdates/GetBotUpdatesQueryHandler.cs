using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Shared.Exceptions.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.GetBotUpdates;

public class GetBotUpdatesQueryHandler : IRequestHandler<GetBotUpdatesQuery, List<Domain.BotUpdate>>
{
    private const int MaxUpdatesLimit = 100;
    private const int MaxLongPollTimeoutSeconds = 50;

    private readonly BotUpdatesStorage _updatesStorage;
    private readonly BotUpdateNotifier _notifier;
    private readonly BotPollingGuard _pollingGuard;

    public GetBotUpdatesQueryHandler(
        BotUpdatesStorage updatesStorage,
        BotUpdateNotifier notifier,
        BotPollingGuard pollingGuard)
    {
        _updatesStorage = updatesStorage;
        _notifier = notifier;
        _pollingGuard = pollingGuard;
    }

    public async Task<List<Domain.BotUpdate>> Handle(GetBotUpdatesQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, MaxUpdatesLimit);
        var timeout = Math.Clamp(request.TimeoutSeconds, 0, MaxLongPollTimeoutSeconds);

        if (!_pollingGuard.TryEnter(request.BotId))
            throw new BotPollingConflictException();

        try
        {
            if (request.Offset > 0)
                await _updatesStorage.Confirm(request.BotId, request.Offset);

            var batch = await _updatesStorage.GetBacklog(request.BotId, request.Offset, limit);

            if (batch.Count == 0 && timeout > 0)
            {
                await _notifier.WaitForUpdateAsync(request.BotId, TimeSpan.FromSeconds(timeout), cancellationToken);
                batch = await _updatesStorage.GetBacklog(request.BotId, request.Offset, limit);
            }

            return batch;
        }
        finally
        {
            _pollingGuard.Exit(request.BotId);
        }
    }
}
