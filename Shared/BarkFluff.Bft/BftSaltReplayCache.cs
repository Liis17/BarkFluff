namespace BarkFluff.Bft;

public enum BftSaltReplayAddResult
{
    Added,
    Duplicate,
    Full
}

public sealed class BftSaltReplayCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(300);
    public const int DefaultCapacity = 1_000_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _salts = new(StringComparer.Ordinal);
    private readonly PriorityQueue<(string Salt, DateTimeOffset ExpiresAt), long> _expirations = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _capacity;

    public BftSaltReplayCache(int capacity = DefaultCapacity, TimeProvider? timeProvider = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int Count
    {
        get { lock (_gate) { PruneExpired(_timeProvider.GetUtcNow()); return _salts.Count; } }
    }

    public BftSaltReplayAddResult TryAdd(ReadOnlySpan<byte> salt)
    {
        if (salt.Length != 32) throw new ArgumentException("BFT salts must be 32 bytes.", nameof(salt));
        var key = Convert.ToHexString(salt);
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            PruneExpired(now);
            if (_salts.ContainsKey(key)) return BftSaltReplayAddResult.Duplicate;
            if (_salts.Count >= _capacity) return BftSaltReplayAddResult.Full;
            var expiresAt = now + DefaultTtl;
            _salts.Add(key, expiresAt);
            _expirations.Enqueue((key, expiresAt), expiresAt.UtcDateTime.Ticks);
            return BftSaltReplayAddResult.Added;
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        while (_expirations.TryPeek(out var entry, out var priority) && priority <= now.UtcDateTime.Ticks)
        {
            _expirations.Dequeue();
            if (_salts.TryGetValue(entry.Salt, out var expiresAt) && expiresAt == entry.ExpiresAt)
                _salts.Remove(entry.Salt);
        }
    }
}
