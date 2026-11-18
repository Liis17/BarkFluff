namespace BarkFluff.BftProxy.Tests;

using System.Threading.Channels;

internal sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly Channel<TimeSpan> _scheduled = Channel.CreateUnbounded<TimeSpan>();
    private DateTimeOffset _now = initialTime;
    private long _timestamp;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public override long GetTimestamp()
    {
        lock (_gate) return _timestamp;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidateTimerDuration(dueTime, nameof(dueTime));
        ValidateTimerDuration(period, nameof(period));
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate) _timers.Add(timer);
        _scheduled.Writer.TryWrite(dueTime);
        return timer;
    }

    public async Task WaitForTimerAsync(TimeSpan dueTime, CancellationToken cancellationToken)
    {
        while (await _scheduled.Reader.ReadAsync(cancellationToken).ConfigureAwait(false) != dueTime) { }
    }

    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount));
        List<(TimerCallback Callback, object? State)> callbacks = [];
        lock (_gate)
        {
            _now += amount;
            _timestamp += amount.Ticks;
            foreach (var timer in _timers)
            {
                if (timer.DueAt is not { } dueAt || dueAt > _now) continue;
                callbacks.Add((timer.Callback, timer.State));
                if (timer.Period > TimeSpan.Zero)
                {
                    do { dueAt += timer.Period; } while (dueAt <= _now);
                    timer.DueAt = dueAt;
                }
                else timer.DueAt = null;
            }
        }
        foreach (var (callback, state) in callbacks) callback(state);
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate) _timers.Remove(timer);
    }

    private void ValidateTimerDuration(TimeSpan duration, string parameterName)
    {
        if (duration < TimeSpan.FromMilliseconds(-1)) throw new ArgumentOutOfRangeException(parameterName);
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private int _disposed;
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public DateTimeOffset? DueAt { get; set; }
        public TimeSpan Period { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            owner.ValidateTimerDuration(dueTime, nameof(dueTime));
            owner.ValidateTimerDuration(period, nameof(period));
            if (Volatile.Read(ref _disposed) != 0) return false;
            lock (owner._gate)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                Period = period;
            }
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
