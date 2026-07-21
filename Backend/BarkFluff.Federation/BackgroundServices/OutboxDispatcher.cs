using System.Diagnostics;

using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Domain.Enums;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Shared.Queue.Federation;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.BackgroundServices;

// Диспетчер outbox (этап 2.2). Гарантирует at-least-once доставку с упорядочиванием
// per-(Destination, ChatId): событие чата попадает в батч только если у чата нет более раннего
// (меньший Id) недоставленного события; события разных чатов едут независимо.
//
// Backoff: 30s → 2m → 10m → 1h → 6h (далее кап 6h); MaxAttempts (по умолчанию эквивалент 7 суток
// окна ретраев, configurable) → DeadLetter. REJECTED → DeadLetter немедленно, очередь чата едет дальше.
public class OutboxDispatcher : BackgroundService
{
    private static readonly TimeSpan LoopInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan[] BackoffSteps =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
    ];
    private const int MaxBatchSize = 100;
    private const int MaxBatchBytes = 1_000_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly FederationSwitch _switch;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        FederationSwitch federationSwitch,
        MetricsCollector metrics,
        ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _switch = federationSwitch;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(LoopInterval);
        do
        {
            try
            {
                await DispatchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка диспетчера outbox Federation");
                _metrics.Increment("outbox_dispatch_errors");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchOnceAsync(CancellationToken ct)
    {
        // P1-04: выключенная/несконфигурированная нода не шлёт исходящий federation-трафик.
        if (!_switch.IsActive)
            return;

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FederationContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<ServerResolver>();
        var channelFactory = scope.ServiceProvider.GetRequiredService<S2SChannelFactory>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var now = DateTime.UtcNow;
        var maxAttempts = GetMaxAttempts();

        // Группировка по Destination — обрабатываем один Destination за раз, чтобы транзакционно
        // применять результаты per-event.
        var destinations = await context.Outbox
            .Where(r => r.Status == OutboxStatus.Pending && r.NextAttemptAt <= now)
            .Select(r => r.Destination)
            .Distinct()
            .ToListAsync(ct);

        foreach (var destination in destinations)
            await DispatchDestinationAsync(context, resolver, channelFactory, publishEndpoint, destination, now, maxAttempts, ct);

        // Gauge: текущая глубина Pending.
        var pendingCount = await context.Outbox.CountAsync(r => r.Status == OutboxStatus.Pending, ct);
        _metrics.Set("outbox_pending", pendingCount);
    }

    private async Task DispatchDestinationAsync(
        FederationContext context,
        ServerResolver resolver,
        S2SChannelFactory channelFactory,
        IPublishEndpoint publishEndpoint,
        string destination,
        DateTime now,
        int maxAttempts,
        CancellationToken ct)
    {
        // Упорядочивание per-chat: событие попадает в батч только если у того же ChatId нет
        // более раннего (меньший Id) недоставленного Pending-события.
        //
        // Реализация: для каждой строки считаем, есть ли Pending-строка с тем же Destination+ChatId,
        // меньшим Id и тем же ChatId (если ChatId = null — отдаём без ограничения).
        var batchRows = await (
            from r in context.Outbox
            where r.Destination == destination && r.Status == OutboxStatus.Pending && r.NextAttemptAt <= now
            where r.ChatId == null || !context.Outbox.Any(earlier =>
                earlier.Destination == destination
                && earlier.ChatId == r.ChatId
                && earlier.Id < r.Id
                && earlier.Status == OutboxStatus.Pending)
            orderby r.ChatId, r.Id
            select r
        ).Take(MaxBatchSize).ToListAsync(ct);

        if (batchRows.Count == 0)
            return;

        // Ограничение по сумме байт.
        var selected = new List<FederationOutbox>();
        var totalBytes = 0;
        foreach (var row in batchRows)
        {
            if (selected.Count > 0 && (totalBytes + row.PayloadBytes.Length > MaxBatchBytes))
                break;
            selected.Add(row);
            totalBytes += row.PayloadBytes.Length;
        }

        // Проверим: пир доступен?
        var server = await resolver.ResolveAsync(destination, ct);
        if (server is null)
        {
            await ApplyTransportFailureAsync(context, selected, "peer_unresolved", now, maxAttempts, ct);
            return;
        }

        DeliverEventsRequest request;
        try
        {
            var events = selected.Select(r => FederationEvent.Parser.ParseFrom(r.PayloadBytes)).ToList();
            request = new DeliverEventsRequest();
            request.Events.AddRange(events);
        }
        catch (Exception ex)
        {
            // Не должны сюда попадать — payload писали сами. Если упало — критическая ошибка, в DLQ.
            _logger.LogError(ex, "Не удалось десериализовать payload outbox для destination={Destination}", destination);
            await ApplyTransportFailureAsync(context, selected, "payload_corrupt", now, maxAttempts, ct);
            return;
        }

        DeliverEventsResponse response;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var invoker = await channelFactory.GetInvokerAsync(destination, ct);
            var client = new FederationS2SApi.FederationS2SApiClient(invoker);
            response = await client.DeliverEventsAsync(request, cancellationToken: ct);
            _metrics.Add("outbox_deliver_duration_ms_total", stopwatch.ElapsedMilliseconds);
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "Транспортная ошибка доставки outbox → {Destination}", destination);
            await ApplyTransportFailureAsync(context, selected, "transport_error", now, maxAttempts, ct);
            return;
        }

        // Per-event обработка. Если в ответе меньше результатов, чем в батче — недостающие считаем RETRY.
        var byEventId = response.Results.ToDictionary(r => r.EventId);
        foreach (var row in selected)
        {
            var eventIdStr = row.EventId.ToString();
            if (!byEventId.TryGetValue(eventIdStr, out var result))
            {
                ApplyRetry(context, row, "no_result", now, maxAttempts);
                continue;
            }

            switch (result.Status)
            {
                case EventStatus.Ok:
                case EventStatus.AlreadyProcessed:
                    row.Status = OutboxStatus.Delivered;
                    row.LastError = null;
                    _metrics.Increment("outbox_delivered");
                    break;

                case EventStatus.Rejected:
                    row.Status = OutboxStatus.DeadLetter;
                    row.LastError = result.ErrorCode;
                    _metrics.Increment("outbox_deadletter.rejected");
                    // Privacy-отказ → FederatedChatRejectedEvent (этап 2.5).
                    if (result.ErrorCode == "FederatedDmRejected" && row.ChatId.HasValue)
                    {
                        _metrics.Increment("outbox_deadletter.federated_dm_rejected");
                        await publishEndpoint.Publish(new FederatedChatRejectedEvent
                        {
                            ChatId = row.ChatId.Value,
                            Reason = result.ErrorCode,
                        }, ct);
                    }
                    break;

                case EventStatus.Retry:
                case EventStatus.Unknown:
                default:
                    ApplyRetry(context, row, string.IsNullOrEmpty(result.ErrorCode) ? "retry" : result.ErrorCode, now, maxAttempts);
                    break;
            }
        }

        await context.SaveChangesAsync(ct);
    }

    private void ApplyRetry(FederationContext context, FederationOutbox row, string reason, DateTime now, int maxAttempts)
    {
        row.Attempts += 1;
        if (row.Attempts >= maxAttempts)
        {
            row.Status = OutboxStatus.DeadLetter;
            row.LastError = $"max_attempts:{reason}";
            row.NextAttemptAt = now + GetBackoff(row.Attempts);
            _metrics.Increment("outbox_deadletter.max_attempts");
            return;
        }

        row.LastError = reason;
        row.NextAttemptAt = now + GetBackoff(row.Attempts);
        _metrics.Increment("outbox_retry");
    }

    private async Task ApplyTransportFailureAsync(
        FederationContext context,
        IReadOnlyList<FederationOutbox> rows,
        string reason,
        DateTime now,
        int maxAttempts,
        CancellationToken ct)
    {
        foreach (var row in rows)
            ApplyRetry(context, row, reason, now, maxAttempts);

        await context.SaveChangesAsync(ct);
    }

    private static TimeSpan GetBackoff(int attempts)
    {
        if (attempts <= 0)
            return BackoffSteps[0];
        var idx = Math.Min(attempts - 1, BackoffSteps.Length - 1);
        return BackoffSteps[idx];
    }

    private int GetMaxAttempts()
    {
        // По умолчанию: 20 попыток по экспоненте до 6ч — кумулятивно ~7 суток окна ретраев.
        const int defaultMaxAttempts = 20;
        var raw = _configuration["Federation:OutboxMaxAttempts"];
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : defaultMaxAttempts;
    }
}
