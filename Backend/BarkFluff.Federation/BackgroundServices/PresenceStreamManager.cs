using System.Collections.Concurrent;

using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Proto.Onliner;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

namespace BarkFluff.Federation.BackgroundServices;

/// <summary>
/// Принимающая сторона presence-моста (этап 4.3): держит по ОДНОМУ S2S-стриму на ноду-партнёра
/// и вливает полученные статусы в Onliner.
/// </summary>
/// <remarks>
/// Один агрегированный стрим на пару нод, а не стрим на подписку: подписчиков много, ноды —
/// единицы, и мультиплексирование через одно соединение снимает и накладные расходы, и лимиты.
///
/// Набор uuid передаётся в самом <c>SubscribePresenceRequest</c> — control-сообщений в v1
/// контракта нет, поэтому обновление набора = переоткрытие стрима (с дебаунсом против флаппинга).
///
/// Ретраев по событиям нет: presence эфемерен. Потеря события допустима, вместо ретраев —
/// реконнект с backoff и периодический ресинк на origin-стороне.
/// </remarks>
public class PresenceStreamManager : BackgroundService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    private sealed class PeerStream
    {
        public required CancellationTokenSource Cancellation { get; init; }
        public required Task Worker { get; init; }
        public required HashSet<Guid> Uuids { get; init; }
        public required DateTime OpenedAt { get; init; }
    }

    private readonly ConcurrentDictionary<string, PeerStream> _streams = new(StringComparer.OrdinalIgnoreCase);

    private readonly PresenceInterestRegistry _interest;
    private readonly RemoteUserServerCache _serverCache;
    private readonly PeerCapabilityCache _capabilities;
    private readonly S2SChannelFactory _channelFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FederationSwitch _switch;
    private readonly PresenceOptions _options;
    private readonly OnlinerServerApi.OnlinerServerApiClient _onlinerClient;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<PresenceStreamManager> _logger;

    public PresenceStreamManager(
        PresenceInterestRegistry interest,
        RemoteUserServerCache serverCache,
        PeerCapabilityCache capabilities,
        S2SChannelFactory channelFactory,
        IServiceScopeFactory scopeFactory,
        FederationSwitch federationSwitch,
        PresenceOptions options,
        OnlinerServerApi.OnlinerServerApiClient onlinerClient,
        MetricsCollector metrics,
        ILogger<PresenceStreamManager> logger)
    {
        _interest = interest;
        _serverCache = serverCache;
        _capabilities = capabilities;
        _channelFactory = channelFactory;
        _scopeFactory = scopeFactory;
        _switch = federationSwitch;
        _options = options;
        _onlinerClient = onlinerClient;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.ReconcileInterval);

        do
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сверки presence-подписок");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        await CloseAllAsync();
    }

    /// <summary>Сверить «желаемое» (union интереса, сгруппированный по нодам) с фактическим.</summary>
    private async Task ReconcileAsync(CancellationToken ct)
    {
        // Гейт: выключенная федерация не держит ни одного стрима.
        if (!_switch.IsActive)
        {
            await CloseAllAsync();
            return;
        }

        var union = _interest.GetUnion();
        _metrics.Set("presence_interest_uuids", union.Count);

        var desired = await _serverCache.GroupByServerAsync(union, ct);

        // Ноды, за которыми больше не следят → закрыть стрим.
        foreach (var serverName in _streams.Keys.Where(s => !desired.ContainsKey(s)).ToList())
        {
            await CloseAsync(serverName);
        }

        foreach (var (serverName, uuids) in desired)
        {
            var limited = uuids.Count > _options.MaxSubscriptionSize
                ? uuids.Take(_options.MaxSubscriptionSize).ToHashSet()
                : uuids.ToHashSet();

            if (uuids.Count > limited.Count)
            {
                // Защита от разрастания, а не отказ: лишние uuid просто не наблюдаются.
                _metrics.Add("presence_subscription_truncated", uuids.Count - limited.Count);
            }

            if (_streams.TryGetValue(serverName, out var existing))
            {
                if (existing.Uuids.SetEquals(limited))
                {
                    continue;
                }

                // Дебаунс: частые изменения набора не должны устраивать флаппинг переоткрытий.
                if (DateTime.UtcNow - existing.OpenedAt < _options.ResubscribeMinInterval)
                {
                    continue;
                }

                await CloseAsync(serverName);
                _metrics.Increment("presence_resubscribes");
            }

            await OpenAsync(serverName, limited, ct);
        }

        _metrics.Set("presence_streams_out", _streams.Count);
    }

    private async Task OpenAsync(string serverName, HashSet<Guid> uuids, CancellationToken ct)
    {
        if (uuids.Count == 0)
        {
            return;
        }

        using (var scope = _scopeFactory.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<ServerResolver>();
            if (await resolver.ResolveAsync(serverName, ct) is null)
            {
                // Неизвестна или заблокирована — не наш партнёр.
                _metrics.Increment("presence_subscribe_rejected.not_resolved");
                return;
            }
        }

        if (!await _capabilities.SupportsAsync(serverName, "presence", ct))
        {
            _metrics.Increment("presence_peer_unsupported");
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _streams[serverName] = new PeerStream
        {
            Cancellation = cts,
            Uuids = uuids,
            OpenedAt = DateTime.UtcNow,
            Worker = RunStreamAsync(serverName, uuids, cts.Token),
        };
    }

    /// <summary>
    /// Живой цикл одного стрима: подписка → чтение событий → при обрыве гашение статусов и
    /// реконнект с экспоненциальным backoff (кап — минута: presence эфемерен, ждать дольше
    /// бессмысленно).
    /// </summary>
    private async Task RunStreamAsync(string serverName, HashSet<Guid> uuids, CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConsumeStreamAsync(serverName, uuids, ct);
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _metrics.Increment("presence_stream_errors");
                _logger.LogWarning(ex, "Presence-стрим ноды {Server} оборвался", serverName);
            }

            // Статусы обязаны погаснуть, а не «залипнуть онлайн»: источник истины недоступен.
            await ExtinguishAsync(uuids, ct);

            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(backoff, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = backoff < MaxBackoff
                ? TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks))
                : MaxBackoff;
        }
    }

    private async Task ConsumeStreamAsync(string serverName, HashSet<Guid> uuids, CancellationToken ct)
    {
        var invoker = await _channelFactory.GetInvokerAsync(serverName, ct);
        var client = new FederationS2SApi.FederationS2SApiClient(invoker);

        var request = new SubscribePresenceRequest();
        request.UserUuids.AddRange(uuids.Select(u => u.ToString()));

        using var call = client.SubscribePresence(request, cancellationToken: ct);

        _logger.LogInformation(
            "Открыт presence-стрим к {Server} на {Count} uuid", serverName, uuids.Count);

        await foreach (var evt in call.ResponseStream.ReadAllAsync(ct))
        {
            if (!Guid.TryParse(evt.UserUuid, out var uuid) || !uuids.Contains(uuid))
            {
                // Партнёр прислал не то, о чём его просили — молча игнорируем.
                continue;
            }

            await UpsertAsync(uuid, MapStatus(evt.Status), evt.LastSeen, ct);
            _metrics.Increment("presence_events_in");
        }
    }

    private async Task ExtinguishAsync(IEnumerable<Guid> uuids, CancellationToken ct)
    {
        foreach (var uuid in uuids)
        {
            try
            {
                await UpsertAsync(uuid, StatusTypeId.Unknown, Timestamp.FromDateTime(DateTime.UtcNow), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Не удалось погасить статус {Uuid}", uuid);
            }
        }
    }

    private Task UpsertAsync(Guid uuid, StatusTypeId status, Timestamp? lastSeen, CancellationToken ct)
        => _onlinerClient.UpsertRemoteStatusAsync(new UpsertRemoteStatusRequest
        {
            UserUuid = uuid.ToString(),
            Status = status,
            LastSeen = lastSeen ?? Timestamp.FromDateTime(DateTime.UtcNow),
        }, cancellationToken: ct).ResponseAsync;

    private static StatusTypeId MapStatus(PresenceStatus status) => status switch
    {
        PresenceStatus.Online => StatusTypeId.StatusOnline,
        PresenceStatus.Offline => StatusTypeId.StatusOffline,
        // «Скрыт privacy» и «статуса нет» с этой стороны неразличимы — так и задумано.
        _ => StatusTypeId.Unknown,
    };

    private async Task CloseAsync(string serverName)
    {
        if (!_streams.TryRemove(serverName, out var stream))
        {
            return;
        }

        await stream.Cancellation.CancelAsync();

        try
        {
            await stream.Worker;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Ожидаемо при закрытии.
        }
        finally
        {
            stream.Cancellation.Dispose();
        }
    }

    private async Task CloseAllAsync()
    {
        foreach (var serverName in _streams.Keys.ToList())
        {
            await CloseAsync(serverName);
        }
    }

}
