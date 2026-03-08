using BarkFluff.Onliner.Domain.Entities;
using BarkFluff.Onliner.Domain.Enums;

using System.Collections.Concurrent;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// In-memory хранилище для быстрого доступа к онлайн-статусам
/// Потокобезопасное, singleton
/// </summary>
public class OnlineStatusStorage
{
    // UserId -> UserOnlineStatus
    private readonly ConcurrentDictionary<long, UserOnlineStatus> _statuses = new();

    /// <summary>
    /// Обновить статус пользователя (Online)
    /// </summary>
    /// <returns>True если статус изменился с Offline/Unknown на Online</returns>
    public bool UpdateStatus(long userId)
    {
        bool statusChanged = false;

        _statuses.AddOrUpdate(
            userId,
            // Add: новая запись
            _ =>
            {
                statusChanged = true;
                return new UserOnlineStatus
                {
                    UserId = userId,
                    Status = StatusTypeId.Online,
                    LastSeen = DateTime.UtcNow
                };
            },
            // Update: существующая запись
            (_, existing) =>
            {
                // Проверяем изменился ли статус
                if (existing.Status != StatusTypeId.Online)
                {
                    statusChanged = true;
                }

                existing.Status = StatusTypeId.Online;
                existing.LastSeen = DateTime.UtcNow;
                return existing;
            }
        );

        return statusChanged;
    }

    /// <summary>
    /// Установить статус Offline
    /// </summary>
    /// <returns>True если статус изменился с Online на Offline</returns>
    public bool SetOffline(long userId)
    {
        bool statusChanged = false;

        _statuses.AddOrUpdate(
            userId,
            // Add: если пользователя нет - создаём с Offline статусом (не должно происходить)
            _ =>
            {
                return new UserOnlineStatus
                {
                    UserId = userId,
                    Status = StatusTypeId.Offline,
                    LastSeen = DateTime.UtcNow
                };
            },
            // Update: существующая запись
            (_, existing) =>
            {
                // Проверяем изменился ли статус
                if (existing.Status == StatusTypeId.Online)
                {
                    statusChanged = true;
                }

                existing.Status = StatusTypeId.Offline;
                existing.LastSeen = DateTime.UtcNow;
                return existing;
            }
        );

        return statusChanged;
    }

    /// <summary>
    /// Получить статус пользователя
    /// </summary>
    public UserOnlineStatus? GetStatus(long userId)
    {
        return _statuses.TryGetValue(userId, out var status)
            ? new UserOnlineStatus
            {
                UserId = status.UserId,
                Status = status.Status,
                LastSeen = status.LastSeen
            }
            : null;
    }

    /// <summary>
    /// Получить все статусы (для bulk save в БД)
    /// </summary>
    public List<UserOnlineStatus> GetAllStatuses()
    {
        return _statuses.Values
            .Select(s => new UserOnlineStatus
            {
                UserId = s.UserId,
                Status = s.Status,
                LastSeen = s.LastSeen
            })
            .ToList();
    }

    /// <summary>
    /// Получить пользователей которые Online дольше заданного времени
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
