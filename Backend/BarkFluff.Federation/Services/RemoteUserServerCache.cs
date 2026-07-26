using System.Collections.Concurrent;

using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Users;

namespace BarkFluff.Federation.Services;

/// <summary>
/// Привязка remote-uuid к его домашней ноде (этап 4.3): Onliner отдаёт интерес плоским списком
/// uuid, а группировку по нодам делает Federation — у него есть Users с таблицей RemoteUsers.
/// </summary>
/// <remarks>
/// Кешируется агрессивно (TTL порядка часа), потому что привязка uuid → нода стабильна by design:
/// смена <c>ServerName</c> для известного uuid запрещена (вопрос №4 из 09-problems-open-questions).
/// Неизвестный uuid (нет в RemoteUsers) не роняет резолв — просто пропускается с метрикой.
/// </remarks>
public class RemoteUserServerCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    private sealed record Entry(string ServerName, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<Guid, Entry> _cache = new();

    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<RemoteUserServerCache> _logger;

    public RemoteUserServerCache(
        UsersServerApi.UsersServerApiClient usersClient,
        MetricsCollector metrics,
        ILogger<RemoteUserServerCache> logger)
    {
        _usersClient = usersClient;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>Сгруппировать uuid по домашним нодам. Uuid без известной ноды выпадают.</summary>
    public async Task<Dictionary<string, List<Guid>>> GroupByServerAsync(
        IReadOnlyCollection<Guid> uuids,
        CancellationToken ct = default)
    {
        var grouped = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);

        if (uuids.Count == 0)
        {
            return grouped;
        }

        var now = DateTime.UtcNow;
        var missing = new List<Guid>();

        foreach (var uuid in uuids)
        {
            if (_cache.TryGetValue(uuid, out var entry) && entry.ExpiresAt > now)
            {
                Add(grouped, entry.ServerName, uuid);
            }
            else
            {
                missing.Add(uuid);
            }
        }

        if (missing.Count > 0)
        {
            foreach (var (uuid, serverName) in await ResolveAsync(missing, ct))
            {
                _cache[uuid] = new Entry(serverName, now + Ttl);
                Add(grouped, serverName, uuid);
            }
        }

        return grouped;
    }

    private async Task<List<(Guid Uuid, string ServerName)>> ResolveAsync(
        IReadOnlyCollection<Guid> uuids,
        CancellationToken ct)
    {
        var resolved = new List<(Guid, string)>();

        try
        {
            var request = new GetUsersByUuidRequest();
            request.Uuids.AddRange(uuids.Select(u => u.ToString()));

            var response = await _usersClient.GetUsersByUuidAsync(request, cancellationToken: ct);

            foreach (var user in response.Users)
            {
                if (!user.Found || !user.IsRemote || string.IsNullOrEmpty(user.ServerName))
                {
                    // Локальный или неизвестный uuid — не наш случай: следить за ним через
                    // федерацию не надо. Не ошибка, просто пропуск.
                    _metrics.Increment("presence_interest_uuid_unknown");
                    continue;
                }

                if (Guid.TryParse(user.Uuid, out var uuid))
                {
                    resolved.Add((uuid, user.ServerName));
                }
            }
        }
        catch (Exception ex)
        {
            // Недоступность Users — временная: следующий цикл сверки повторит резолв.
            _metrics.Increment("presence_interest_resolve_errors");
            _logger.LogWarning(ex, "Не удалось резолвнуть ноды для {Count} remote-uuid", uuids.Count);
        }

        return resolved;
    }

    private static void Add(Dictionary<string, List<Guid>> grouped, string serverName, Guid uuid)
    {
        if (!grouped.TryGetValue(serverName, out var list))
        {
            list = [];
            grouped[serverName] = list;
        }

        list.Add(uuid);
    }
}
