using System.Collections.Concurrent;

namespace BarkFluff.Federation.Services;

/// <summary>
/// Кеш результата валидации входящего typing (этап 4.4): ключ (origin, sender_uuid, chat_id).
/// </summary>
/// <remarks>
/// Typing-heartbeat приходит каждые 4–5 секунд — без кеша каждый порождал бы два внутренних
/// gRPC-вызова (Users + Messages). Отрицательный результат кешируется тоже, но короче: иначе
/// спамящая нода бесплатно нагружала бы Users/Messages бесконечно.
/// </remarks>
public class TypingValidationCache
{
    private sealed record Entry(bool Allowed, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly PresenceOptions _options;
    private readonly TimeProvider _time;

    public TypingValidationCache(PresenceOptions options, TimeProvider? timeProvider = null)
    {
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>null — в кеше ничего нет, валидацию надо выполнить.</summary>
    public bool? TryGet(string origin, Guid senderUuid, string chatId)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        if (!_entries.TryGetValue(Key(origin, senderUuid, chatId), out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= now)
        {
            _entries.TryRemove(Key(origin, senderUuid, chatId), out _);
            return null;
        }

        return entry.Allowed;
    }

    public void Set(string origin, Guid senderUuid, string chatId, bool allowed)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var ttl = allowed
            ? _options.TypingValidationCacheTtl
            : _options.TypingValidationCacheTtl / 2;

        _entries[Key(origin, senderUuid, chatId)] = new Entry(allowed, now + ttl);
    }

    private static string Key(string origin, Guid senderUuid, string chatId)
        => $"{origin}|{senderUuid}|{chatId}";
}
