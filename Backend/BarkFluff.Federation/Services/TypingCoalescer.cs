using System.Collections.Concurrent;

namespace BarkFluff.Federation.Services;

/// <summary>
/// Душит частые typing-heartbeat'ы на исходящем пути (этап 4.4): не чаще одной отправки
/// в окно на ключ (chat, sender, destination).
/// </summary>
/// <remarks>
/// Клиент шлёт heartbeat каждые 4–5 секунд, пока печатает; при нескольких открытых чатах
/// это заметный поток S2S-вызовов на ровном месте.
///
/// In-memory, а не Redis: состояние живёт секунды, потеря при рестарте безвредна (в худшем
/// случае уйдёт один лишний вызов), а persistent-хранилище тут — чистая избыточность.
/// Чистка ленивая: словарь растёт только по активным парам собеседников.
/// </remarks>
public class TypingCoalescer
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, DateTime> _lastSentAt = new();
    private readonly TimeProvider _time;
    private DateTime _lastSweepAt;

    public TypingCoalescer(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _lastSweepAt = _time.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// Можно ли отправлять прямо сейчас. <paramref name="isCancellation"/> проходит всегда:
    /// иначе индикатор у собеседника гас бы только по клиентскому таймауту.
    /// </summary>
    public bool ShouldSend(string chatId, Guid senderUuid, string destination, TimeSpan window, bool isCancellation)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var key = $"{chatId}|{senderUuid}|{destination}";

        SweepIfDue(now);

        if (isCancellation)
        {
            _lastSentAt[key] = now;
            return true;
        }

        if (_lastSentAt.TryGetValue(key, out var lastSent) && now - lastSent < window)
        {
            return false;
        }

        _lastSentAt[key] = now;
        return true;
    }

    private void SweepIfDue(DateTime now)
    {
        if (now - _lastSweepAt < StaleAfter)
        {
            return;
        }

        _lastSweepAt = now;

        foreach (var (key, lastSent) in _lastSentAt)
        {
            if (now - lastSent >= StaleAfter)
            {
                _lastSentAt.TryRemove(key, out _);
            }
        }
    }
}
