using System.Buffers.Binary;

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
    private readonly HashSet<SaltKey> _salts = [];
    private readonly Queue<(SaltKey Key, long AddedAt)> _entries = new();
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
        get { lock (_gate) { PruneExpired(_timeProvider.GetTimestamp()); return _salts.Count; } }
    }

    public BftSaltReplayAddResult TryAdd(ReadOnlySpan<byte> salt)
    {
        if (salt.Length != 32) throw new ArgumentException("BFT salts must be 32 bytes.", nameof(salt));
        var key = new SaltKey(
            BinaryPrimitives.ReadUInt64LittleEndian(salt),
            BinaryPrimitives.ReadUInt64LittleEndian(salt[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(salt[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(salt[24..]));
        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            PruneExpired(now);
            if (_salts.Contains(key)) return BftSaltReplayAddResult.Duplicate;
            if (_salts.Count >= _capacity) return BftSaltReplayAddResult.Full;
            _salts.Add(key);
            _entries.Enqueue((key, now));
            return BftSaltReplayAddResult.Added;
        }
    }

    private void PruneExpired(long now)
    {
        while (_entries.TryPeek(out var entry) && _timeProvider.GetElapsedTime(entry.AddedAt, now) >= DefaultTtl)
        {
            _entries.Dequeue();
            _salts.Remove(entry.Key);
        }
    }

    private readonly record struct SaltKey(ulong First, ulong Second, ulong Third, ulong Fourth);
}
