using BarkFluff.Proto.Onliner;
using BarkFluff.WebApi.Core.MessengerData;

using System.Collections.Concurrent;

using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public sealed class OnlinePresenceService : IOnlinePresenceService
{
    /// <summary>Интервал keepalive задан сервером: без пинга чаще этого клиент считается офлайном.</summary>
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StreamStopTimeout = TimeSpan.FromSeconds(1);

    private readonly WebApiClient _webApi;
    private readonly IClientSession _session;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ConcurrentDictionary<long, UserPresence> _presence = new();

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _statusTask;
    private Task? _keepAliveTask;
    private long[] _watchedUserIds = [];
    private bool _isDisposed;

    public OnlinePresenceService(WebApiClient webApi, IClientSession session)
    {
        _webApi = webApi;
        _session = session;
        _webApi.TokenRefreshed += OnTokenRefreshed;
    }

    public event EventHandler<UserPresence>? PresenceChanged;

    public UserPresence? TryGet(long userId) => _presence.TryGetValue(userId, out var presence) ? presence : null;

    public async Task WatchAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_session.CurrentConnection?.ConnectionParameters is not { } parameters)
        {
            return;
        }

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            var requested = userIds.Distinct().Order().ToArray();
            if (_statusTask is not null && _watchedUserIds.SequenceEqual(requested))
            {
                return;
            }

            // Набор наблюдаемых пользователей меняется на живом стриме: пересоздание рвало бы
            // соединение при каждом изменении списка чатов.
            if (_statusTask is not null)
            {
                _watchedUserIds = requested;
                var changeError = await _webApi.ChangeUsersInSubscription([.. requested], parameters);
                if (changeError.IsSuccess)
                {
                    await ApplySnapshotAsync(requested, parameters);
                }

                return;
            }

            _watchedUserIds = requested;
            _cancellationTokenSource = new CancellationTokenSource();
            _statusTask = ListenOnlineStatusesAsync(requested, parameters, _cancellationTokenSource.Token);
            _keepAliveTask = KeepAliveAsync(parameters, _cancellationTokenSource.Token);
            await ApplySnapshotAsync(requested, parameters);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _webApi.TokenRefreshed -= OnTokenRefreshed;
        await StopAsync();
        _lifecycleLock.Dispose();
    }

    /// <summary>
    /// Для локальных пользователей стрим отдаёт только изменения статуса, без начального снимка,
    /// поэтому текущее состояние приходится дозапрашивать унарным вызовом. Иначе шапка чата
    /// оставалась бы пустой до первого переключения собеседника.
    /// </summary>
    private async Task ApplySnapshotAsync(long[] userIds, GlobalParam parameters)
    {
        if (userIds.Length == 0)
        {
            return;
        }

        var (error, response) = await _webApi.GetOnlineStatus([.. userIds], parameters);
        if (!error.IsSuccess || response is null)
        {
            return;
        }

        foreach (var status in response.UsersStatuses)
        {
            Publish(status);
        }
    }

    private void Publish(UserOnlineStatus status)
    {
        var presence = new UserPresence(
            status.UserId,
            status.Status == StatusTypeId.StatusOnline,
            status.LastSeen?.ToDateTimeOffset() ?? DateTimeOffset.MinValue);
        _presence[presence.UserId] = presence;
        PresenceChanged?.Invoke(this, presence);
    }

    private async Task ListenOnlineStatusesAsync(long[] userIds, GlobalParam parameters, CancellationToken cancellationToken)
    {
        var (error, stream) = await _webApi.SubscribeToOnlineStatus([.. userIds], parameters, cancellationToken);
        if (!error.IsSuccess || stream is null)
        {
            return;
        }

        try
        {
            await foreach (var status in stream.WithCancellation(cancellationToken))
            {
                Publish(status);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
        }
    }

    private async Task KeepAliveAsync(GlobalParam parameters, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(KeepAliveInterval);
        try
        {
            do
            {
                await _webApi.SetOnlineStatus(parameters);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
        }
    }

    private async Task StopCoreAsync()
    {
        var cancellation = _cancellationTokenSource;
        _cancellationTokenSource = null;
        cancellation?.Cancel();

        var tasks = new[] { _statusTask, _keepAliveTask }.Where(task => task is not null).Cast<Task>().ToArray();
        _statusTask = null;
        _keepAliveTask = null;
        _watchedUserIds = [];
        if (tasks.Length > 0)
        {
            // Как и у стримов сообщений: gRPC-вызов не всегда отпускает по отмене, а этот метод
            // вызывается при завершении приложения — бесконечное ожидание не даёт процессу закрыться.
            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(StreamStopTimeout));
        }

        cancellation?.Dispose();
    }

    private void OnTokenRefreshed(object? sender, EventArgs eventArgs)
    {
        if (!_isDisposed)
        {
            _ = RestartAfterTokenRefreshAsync();
        }
    }

    private async Task RestartAfterTokenRefreshAsync()
    {
        try
        {
            var userIds = _watchedUserIds;
            if (userIds.Length == 0)
            {
                return;
            }

            await StopAsync();
            await WatchAsync(userIds);
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
