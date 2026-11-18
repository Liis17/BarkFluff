using BarkFluff.Bft;
using Xunit;

namespace BarkFluff.Bft.Tests;

public sealed class BftKeepAliveAndCancellationTests
{
    private static readonly byte[] ExitPrivateKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] ClientEphemeral = Enumerable.Range(0x61, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] ServerEphemeral = Enumerable.Range(0x81, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] ObfsKey = Enumerable.Range(0x20, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] TlsPin = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public async Task KeepAlive_PingPongInBothDirections()
    {
        var clientClock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var serverClock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var pair = await ConnectAsync(clientClock, serverClock, timeout.Token);
        await pair.Client.Connection.WaitForIncomingMessageCountAsync(1, timeout.Token);
        await pair.Server.Connection.WaitForOutgoingMessageCountAsync(1, timeout.Token);
        await clientClock.WaitForActiveTimerAsync(TimeSpan.FromSeconds(30), timeout.Token);
        await serverClock.WaitForActiveTimerAsync(TimeSpan.FromSeconds(30), timeout.Token);

        clientClock.Advance(TimeSpan.FromSeconds(30));
        await pair.Client.Connection.WaitForOutgoingMessageCountAsync(1, timeout.Token);
        await pair.Server.Connection.WaitForOutgoingMessageCountAsync(2, timeout.Token);
        await pair.Client.Connection.WaitForIncomingMessageCountAsync(2, timeout.Token);
        await pair.Server.Connection.WaitForIncomingMessageCountAsync(1, timeout.Token);

        serverClock.Advance(TimeSpan.FromSeconds(30));
        await pair.Server.Connection.WaitForOutgoingMessageCountAsync(3, timeout.Token);
        await pair.Client.Connection.WaitForOutgoingMessageCountAsync(2, timeout.Token);
        await pair.Client.Connection.WaitForIncomingMessageCountAsync(3, timeout.Token);
        await pair.Server.Connection.WaitForIncomingMessageCountAsync(2, timeout.Token);
    }

    [Fact]
    public async Task KeepAlive_ClosesConnectionWhenPongIsMissing()
    {
        var clientClock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var serverClock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var pair = await ConnectAsync(clientClock, serverClock, timeout.Token);
        await pair.Client.Connection.WaitForIncomingMessageCountAsync(1, timeout.Token);
        await serverClock.WaitForActiveTimerAsync(TimeSpan.FromSeconds(30), timeout.Token);

        clientClock.Advance(TimeSpan.FromSeconds(30));
        await pair.Server.Connection.WaitForIncomingMessageCountAsync(1, timeout.Token);
        await pair.Client.Connection.WaitForIncomingMessageCountAsync(2, timeout.Token);
        pair.ClientStream.DropOutputAfter(0);

        serverClock.Advance(TimeSpan.FromSeconds(30));
        await pair.Server.Connection.WaitForOutgoingMessageCountAsync(3, timeout.Token);
        await serverClock.WaitForActiveTimerAsync(TimeSpan.FromSeconds(20), timeout.Token);
        serverClock.Advance(TimeSpan.FromSeconds(20));
        await pair.Server.Connection.WaitForClosedAsync(timeout.Token);
    }

    [Fact]
    public async Task StreamReadAndFlowControlledWriteHonorCancellation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var pair = await ConnectAsync(TimeProvider.System, TimeProvider.System, timeout.Token, initialWindow: 16384);
        var clientStream = await pair.Client.OpenStreamAsync(1, timeout.Token);
        var serverStream = await pair.Server.AcceptStreamAsync(timeout.Token);

        using var readCancellation = new CancellationTokenSource();
        var readTask = clientStream.ReadAsync(new byte[1], readCancellation.Token).AsTask();
        readCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await readTask);

        using var writeCancellation = new CancellationTokenSource();
        var writeTask = clientStream.WriteAsync(new byte[64 * 1024], writeCancellation.Token).AsTask();
        await pair.Client.Connection.WaitForOutgoingMessageCountAsync(2, timeout.Token);
        await pair.Server.Connection.WaitForIncomingMessageCountAsync(2, timeout.Token);
        writeCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await writeTask);

        await serverStream.ResetAsync(BftResetCode.Cancel);
    }

    private static async Task<TunnelPair> ConnectAsync(TimeProvider clientTimeProvider, TimeProvider serverTimeProvider, CancellationToken cancellationToken, int initialWindow = 262144)
    {
        var (clientStream, serverStream) = TestDuplexStream.CreatePair();
        var serverTask = BftServerTunnel.TryAcceptAsync(serverStream, new BftServerTunnelOptions
        {
            Handshake = new BftServerHandshakeOptions
            {
                StaticPrivateKey = ExitPrivateKey,
                ObfsKeys = [new("keepalive-user", ObfsKey)],
                TimeProvider = serverTimeProvider,
                EphemeralPrivateKeyFactory = () => ServerEphemeral.ToArray()
            },
            ProxyId = "lv-1",
            ServiceIds = [1, 4, 5],
            TlsSpkiSha256 = TlsPin,
            InitialWindow = initialWindow
        }, cancellationToken).AsTask();
        var clientTask = BftClientTunnel.ConnectAsync(clientStream, new BftClientTunnelOptions
        {
            Handshake = new BftClientHandshakeOptions
            {
                StaticPublicKey = BftCrypto.GetX25519PublicKey(ExitPrivateKey),
                ObfsKey = ObfsKey,
                TimeProvider = clientTimeProvider,
                EphemeralPrivateKeyFactory = () => ClientEphemeral.ToArray()
            }
        }, cancellationToken).AsTask();
        await Task.WhenAll(serverTask, clientTask);
        var result = await serverTask;
        Assert.True(result.Accepted, result.HandshakeRejection?.ToString());
        return new TunnelPair(await clientTask, result.Tunnel!, clientStream, serverStream);
    }

    private sealed class TunnelPair : IAsyncDisposable
    {
        private readonly TestDuplexStream _serverStream;

        public TunnelPair(BftClientTunnel client, BftServerTunnel server, TestDuplexStream clientStream, TestDuplexStream serverStream)
        {
            Client = client;
            Server = server;
            ClientStream = clientStream;
            _serverStream = serverStream;
        }

        public BftClientTunnel Client { get; }
        public BftServerTunnel Server { get; }
        public TestDuplexStream ClientStream { get; }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
            await ClientStream.DisposeAsync();
            await _serverStream.DisposeAsync();
        }
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private TaskCompletionSource _timerChanged = NewSignal();
    private DateTimeOffset _now = initialTime;

    public async Task WaitForActiveTimerAsync(TimeSpan dueTime, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_timers.Any(timer => timer.DueTime == dueTime)) return;
                changed = _timerChanged.Task;
            }
            await changed.WaitAsync(cancellationToken);
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            _timers.Add(timer);
            SignalTimerChangedLocked();
            return timer;
        }
    }

    public void Advance(TimeSpan duration)
    {
        List<(TimerCallback Callback, object? State)> callbacks = [];
        lock (_gate)
        {
            _now += duration;
            foreach (var timer in _timers)
                timer.CollectDueCallbacks(_now, callbacks);
        }
        foreach (var (callback, state) in callbacks) callback(state);
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
            SignalTimerChangedLocked();
        }
    }

    private void SignalTimerChangedLocked()
    {
        var previous = _timerChanged;
        _timerChanged = NewSignal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private DateTimeOffset? _dueAt;
        private TimeSpan _period;
        private bool _disposed;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            SetLocked(owner._now, dueTime, period);
        }

        public TimeSpan DueTime => _dueAt is { } dueAt ? dueAt - _owner._now : Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._gate)
            {
                if (_disposed) return false;
                SetLocked(_owner._now, dueTime, period);
                _owner.SignalTimerChangedLocked();
                return true;
            }
        }

        public void CollectDueCallbacks(DateTimeOffset now, List<(TimerCallback Callback, object? State)> callbacks)
        {
            while (!_disposed && _dueAt is { } dueAt && dueAt <= now)
            {
                callbacks.Add((_callback, _state));
                _dueAt = _period == Timeout.InfiniteTimeSpan ? null : dueAt + _period;
            }
        }

        private void SetLocked(DateTimeOffset now, TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;
        }

        public void Dispose()
        {
            lock (_owner._gate) _disposed = true;
            _owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
