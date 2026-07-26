using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.FederationInternal;

using Grpc.Core;

namespace BarkFluff.Onliner.BackgroundServices;

/// <summary>
/// Сообщает Federation, за какими remote-uuid следят подписчики ЭТОГО инстанса (этап 4.2).
/// </summary>
/// <remarks>
/// Передаётся ПОЛНЫЙ набор, а не дельты: Onliner масштабируется горизонтально, стримы
/// подписчиков живут на разных инстансах, и свести «+uuid/−uuid» от нескольких инстансов без
/// общего состояния невозможно. Federation объединяет наборы живых инстансов, протухшие
/// выпадают по TTL — рестарт инстанса самолечится, ретраи не нужны.
///
/// Пустой набор тоже отправляется: это сигнал «за нами больше никто не следит»,
/// по которому Federation закрывает S2S-подписку.
/// </remarks>
public class PresenceInterestReporter : BackgroundService
{
    private readonly OnlineStatusSubscriptionsManager _subscriptionsManager;
    private readonly FederationInternalApi.FederationInternalApiClient _federationClient;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<PresenceInterestReporter> _logger;
    private readonly TimeSpan _interval;

    public PresenceInterestReporter(
        OnlineStatusSubscriptionsManager subscriptionsManager,
        FederationInternalApi.FederationInternalApiClient federationClient,
        IConfiguration configuration,
        MetricsCollector metrics,
        ILogger<PresenceInterestReporter> logger)
    {
        _subscriptionsManager = subscriptionsManager;
        _federationClient = federationClient;
        _metrics = metrics;
        _logger = logger;

        var seconds = configuration.GetValue<int?>("Onliner:PresenceInterestIntervalSeconds") ?? 20;
        _interval = TimeSpan.FromSeconds(seconds > 0 ? seconds : 20);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Presence interest reporter started, interval {Interval}s", _interval.TotalSeconds);

        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ReportAsync(stoppingToken);
        }
    }

    private async Task ReportAsync(CancellationToken stoppingToken)
    {
        var uuids = _subscriptionsManager.GetTrackedUuids();

        try
        {
            var request = new SetPresenceInterestRequest { InstanceId = InstanceId.Current };
            request.UserUuids.AddRange(uuids.Select(uuid => uuid.ToString()));

            await _federationClient.SetPresenceInterestAsync(
                request, cancellationToken: stoppingToken);

            _metrics.Increment("presence_interest_reports");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
        {
            // Federation ещё не реализовал RPC (до этапа 4.3) — это не ошибка эксплуатации.
            _logger.LogDebug("Federation does not implement SetPresenceInterest yet");
        }
        catch (Exception ex)
        {
            // Ретраев нет by design: presence эфемерен, следующий тик через N секунд.
            _metrics.Increment("presence_interest_errors");
            _logger.LogWarning(ex,
                "Failed to report presence interest ({Count} uuids) to Federation", uuids.Count);
        }
    }
}
