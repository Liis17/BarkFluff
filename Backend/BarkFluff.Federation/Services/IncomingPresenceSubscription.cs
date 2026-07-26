namespace BarkFluff.Federation.Services;

/// <summary>
/// Одна входящая S2S-подписка на presence: нода-партнёр следит за набором НАШИХ пользователей
/// (этап 4.3). Живёт столько же, сколько gRPC-стрим.
/// </summary>
/// <remarks>
/// Coalescing реализован «грязными» пометками, а не очередью событий: на каждого пользователя
/// в окне уходит ровно одно событие, и оно несёт ПОСЛЕДНЕЕ состояние — потому что статус
/// перечитывается у Onliner в момент отправки, а не берётся из события. Так N изменений подряд
/// стоят одного вызова, и порядок не может «перевернуться».
/// </remarks>
public sealed class IncomingPresenceSubscription
{
    private readonly HashSet<long> _dirty = [];
    private readonly Dictionary<long, DateTime> _lastSentAt = [];
    private readonly Lock _gate = new();

    public IncomingPresenceSubscription(string origin, IReadOnlyDictionary<long, Guid> watched)
    {
        Origin = origin;
        Watched = watched;
        StartedAt = DateTime.UtcNow;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string Origin { get; }

    public DateTime StartedAt { get; }

    /// <summary>Наши пользователи, разрешённые к наблюдению: локальный userId → его uuid.</summary>
    public IReadOnlyDictionary<long, Guid> Watched { get; }

    public bool Watches(long userId) => Watched.ContainsKey(userId);

    public void MarkDirty(long userId)
    {
        if (!Watches(userId))
        {
            return;
        }

        lock (_gate)
        {
            _dirty.Add(userId);
        }
    }

    /// <summary>Пометить всех — периодический ресинк и начальный снимок.</summary>
    public void MarkAllDirty()
    {
        lock (_gate)
        {
            foreach (var userId in Watched.Keys)
            {
                _dirty.Add(userId);
            }
        }
    }

    /// <summary>
    /// Забрать пользователей, которым пора отправить статус: помечены грязными и с прошлой
    /// отправки прошло не меньше окна coalescing. Остальные остаются грязными до своего окна.
    /// </summary>
    public List<long> TakeDue(DateTime now, TimeSpan coalesceWindow)
    {
        lock (_gate)
        {
            if (_dirty.Count == 0)
            {
                return [];
            }

            var due = new List<long>();

            foreach (var userId in _dirty)
            {
                if (!_lastSentAt.TryGetValue(userId, out var lastSent) || now - lastSent >= coalesceWindow)
                {
                    due.Add(userId);
                }
            }

            foreach (var userId in due)
            {
                _dirty.Remove(userId);
                _lastSentAt[userId] = now;
            }

            return due;
        }
    }
}
