using BarkFluff.Onliner.Domain.Entities;
using BarkFluff.Onliner.Domain.Enums;

using System.Collections.Concurrent;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// In-memory хранилище для быстрого доступа к онлайн-статусам
/// Потокобезопасное, singleton. Записи иммутабельны — обновление через CAS-замену.
/// </summary>
public class OnlineStatusStorage
{
    private readonly ConcurrentDictionary<long, UserOnlineStatus> _statuses = new();

    /// <summary>
    /// Обновить статус пользователя (Online).
    /// </summary>
    /// <returns>True если статус изменился с Offline/Unknown на Online.</returns>
    public bool UpdateStatus(long userId)
    {
        while (true)
        {
            var now = DateTime.UtcNow;

            if (_statuses.TryGetValue(userId, out var existing))
            {
                var updated = existing with { Status = StatusTypeId.Online, LastSeen = now };

                if (_statuses.TryUpdate(userId, updated, existing))
                {
                    return existing.Status != StatusTypeId.Online;
                }
            }
            else
            {
                var newStatus = new UserOnlineStatus
                {
                    UserId = userId,
                    Status = StatusTypeId.Online,
                    LastSeen = now
                };

                if (_statuses.TryAdd(userId, newStatus))
                {
                    return true;
                }
            }
        }
    }

    /// <summary>
    /// Установить статус Offline.
    /// </summary>
    /// <returns>True если статус изменился с Online на Offline.</returns>
    public bool SetOffline(long userId)
    {
        while (true)
        {
            var now = DateTime.UtcNow;

            if (_statuses.TryGetValue(userId, out var existing))
            {
                var updated = existing with { Status = StatusTypeId.Offline, LastSeen = now };

                if (_statuses.TryUpdate(userId, updated, existing))
                {
                    return existing.Status == StatusTypeId.Online;
                }
            }
            else
            {
                var newStatus = new UserOnlineStatus
                {
                    UserId = userId,
                    Status = StatusTypeId.Offline,
                    LastSeen = now
                };

                if (_statuses.TryAdd(userId, newStatus))
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Получить статус пользователя.
    /// </summary>
    public UserOnlineStatus? GetStatus(long userId)
    {
        return _statuses.TryGetValue(userId, out var status) ? status : null;
    }

    /// <summary>
    /// Получить все статусы (для bulk save в БД).
    /// Возвращает иммутабельные снимки — копировать не требуется.
    /// </summary>
    public IReadOnlyCollection<UserOnlineStatus> GetAllStatuses()
    {
        return _statuses.Values.ToList();
    }

    /// <summary>
    /// Получить пользователей которые Online дольше заданного времени.
    /// </summary>
    public List<long> GetOnlineUsersOlderThan(TimeSpan threshold)
    {
        var cutoffTime = DateTime.UtcNow - threshold;

        return _statuses
            .Where(kvp => kvp.Value.Status == StatusTypeId.Online
                       && kvp.Value.LastSeen < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();
    }
}
