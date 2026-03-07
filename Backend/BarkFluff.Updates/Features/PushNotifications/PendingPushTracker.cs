using System.Collections.Concurrent;

namespace BarkFluff.Updates.Features.PushNotifications;

/// <summary>
/// Отслеживает ожидающие push-уведомления.
/// Позволяет отменить отправку push, если сообщение было прочитано в течение 5 секунд.
/// </summary>
public class PendingPushTracker
{
    private readonly ConcurrentDictionary<(long MessageId, long UserId), CancellationTokenSource> _pendingPushes = new();

    /// <summary>
    /// Регистрирует ожидающее push-уведомление и возвращает CancellationTokenSource для управления.
    /// </summary>
    public CancellationTokenSource RegisterPending(long messageId, long userId)
    {
        var key = (messageId, userId);
        var cts = new CancellationTokenSource();

        // Если уже есть запись, отменяем старую
        if (_pendingPushes.TryRemove(key, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        _pendingPushes[key] = cts;
        return cts;
    }

    /// <summary>
    /// Отменяет ожидающее push-уведомление (сообщение было прочитано).
    /// </summary>
    public void CancelPending(long messageId, long userId)
    {
        var key = (messageId, userId);

        if (_pendingPushes.TryRemove(key, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>
    /// Удаляет запись об ожидающем push-уведомлении.
    /// </summary>
    public void RemovePending(long messageId, long userId)
    {
        var key = (messageId, userId);

        if (_pendingPushes.TryRemove(key, out var cts))
        {
            cts.Dispose();
        }
    }
}
