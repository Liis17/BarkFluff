using System.Collections.Concurrent;

using BarkFluff.GrpcServer.Metrics;

namespace BarkFluff.Federation.Services;

/// <summary>
/// Circuit breaker скачивания файлов per-origin (этап 3.5): лежащая нода не должна
/// превращать каждое обращение в ожидание connect-timeout.
/// </summary>
/// <remarks>
/// Считаются только <b>транспортные</b> неудачи. Отказ живой ноды (<c>PermissionDenied</c>,
/// <c>NotFound</c>) сбоем не является: приватный аватар или чужой файл не значат, что нода
/// недоступна, и не должны блокировать скачивание остальных файлов оттуда же.
///
/// In-memory и без внешних библиотек: состояние живёт секунды-минуты, рестарт сервиса
/// с чистым circuit'ом — допустимое поведение (максимум один лишний поход в сеть).
/// </remarks>
public class RemoteFileCircuitBreaker
{
    private sealed class State
    {
        public int ConsecutiveFailures;
        public DateTime OpenUntil;
    }

    private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);

    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly MetricsCollector _metrics;
    private readonly TimeProvider _time;

    public RemoteFileCircuitBreaker(
        IConfiguration configuration,
        MetricsCollector metrics,
        TimeProvider? timeProvider = null)
    {
        _failureThreshold = Read(configuration, "Federation:RemoteFileCircuitFailures", 3);
        _openDuration = TimeSpan.FromSeconds(Read(configuration, "Federation:RemoteFileCircuitOpenSeconds", 60));
        _metrics = metrics;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Можно ли идти в сеть. false — circuit открыт, отвечаем сразу, не тратя connect-timeout.
    /// По истечении окна пропускается ровно один пробный запрос (half-open).
    /// </summary>
    public bool TryEnter(string serverName)
    {
        var state = _states.GetOrAdd(serverName, _ => new State());
        var now = _time.GetUtcNow().UtcDateTime;

        lock (state)
        {
            // Окно истекло → пропускаем запрос. Он и есть пробный (half-open): успех
            // закроет circuit, неудача откроет его на новое окно.
            return state.OpenUntil <= now;
        }
    }

    public void RecordSuccess(string serverName)
    {
        var state = _states.GetOrAdd(serverName, _ => new State());

        lock (state)
        {
            state.ConsecutiveFailures = 0;
            state.OpenUntil = DateTime.MinValue;
        }
    }

    /// <summary>Транспортная неудача: сеть, TLS, connect-timeout, молчание origin.</summary>
    public void RecordFailure(string serverName)
    {
        var state = _states.GetOrAdd(serverName, _ => new State());
        var now = _time.GetUtcNow().UtcDateTime;

        lock (state)
        {
            state.ConsecutiveFailures++;

            if (state.ConsecutiveFailures >= _failureThreshold)
            {
                state.OpenUntil = now + _openDuration;
                _metrics.Increment("remote_file_circuit_open." + serverName);
            }
        }
    }

    /// <summary>Открыт ли circuit прямо сейчас — для диагностики и тестов.</summary>
    public bool IsOpen(string serverName)
    {
        if (!_states.TryGetValue(serverName, out var state))
        {
            return false;
        }

        lock (state)
        {
            return state.OpenUntil > _time.GetUtcNow().UtcDateTime;
        }
    }

    private static int Read(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration.GetValue<int?>(key) ?? fallback;
        return value > 0 ? value : fallback;
    }
}
