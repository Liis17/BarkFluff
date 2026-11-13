using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace BarkFluff.Bft;

internal sealed class BftMuxStreamState(uint id, byte serviceId, int initialWindow)
{
    public uint Id { get; } = id;
    public byte ServiceId { get; } = serviceId;
    public long SendWindow = initialWindow;
    public long ReceiveWindow = initialWindow;
    public int BufferedBytes;
    public int PendingOutboundBytes;
    public int PendingWindowUpdateBytes;
    public Queue<byte[]> Incoming { get; } = new();
    public Queue<BftFrame> Outgoing { get; } = new();
    public SemaphoreSlim IncomingSignal { get; } = new(0);
    public SemaphoreSlim WindowSignal { get; } = new(0);
    public bool InRoundRobin;
    public bool RemoteClosed;
    public bool LocalClosed;
    public bool LocalCloseSent;
    public bool Reset;
    public Exception? Failure;
}

internal enum BftMuxFrameAction
{
    Processed,
    Ignored,
    ResetServiceUnavailable,
    ResetStreamLimit,
    ResetFlowControl,
    ResetStreamClosed,
    AuthenticationRejected
}

internal sealed class BftMuxConnection : IAsyncDisposable
{
    private static readonly TimeSpan KeepAliveIdle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(20);

    private readonly object _gate = new();
    private readonly BftNoiseSession _session;
    private readonly bool _isServer;
    private int _initialWindow;
    private int _maxStreams;
    private readonly HashSet<byte> _serviceIds;
    private readonly string _proxyId;
    private readonly byte[][] _accessTokens;
    private readonly byte[] _tlsSpkiSha256;
    private readonly bool _authRequired;
    private readonly TimeProvider _timeProvider;
    private readonly IBftRandomSource _random;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writerSignal = new(0);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Dictionary<uint, BftMuxStreamState> _streams = new();
    private readonly Queue<BftFrame> _controlFrames = new();
    private readonly List<uint> _roundRobin = new();
    private readonly Channel<BftTunnelStream> _acceptedStreams = Channel.CreateUnbounded<BftTunnelStream>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<uint> _goAwayReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _streamsChanged = NewSignal();
    private Task? _readerTask;
    private Task? _writerTask;
    private Task? _keepAliveTask;
    private BftHello? _hello;
    private DateTimeOffset _lastReceived;
    private byte[]? _pendingPing;
    private uint _maxOpened;
    private uint _goAwayLastStreamId;
    private bool _hasReceivedGoAway;
    private bool _sentGoAway;
    private bool _readyAccepted;
    private bool _disposed;
    private bool _aborted;
    private Exception? _failure;
    private int _roundRobinIndex;

    public BftMuxConnection(BftNoiseSession session, bool isServer, string proxyId, IEnumerable<byte> serviceIds,
        int initialWindow, int maxStreams, IEnumerable<string>? accessTokens, TimeProvider timeProvider, IBftRandomSource random,
        byte[]? tlsSpkiSha256 = null)
    {
        _session = session;
        _isServer = isServer;
        _proxyId = proxyId;
        _serviceIds = new HashSet<byte>(serviceIds);
        _initialWindow = initialWindow;
        _maxStreams = maxStreams;
        _accessTokens = (accessTokens ?? []).Select(Encoding.UTF8.GetBytes).ToArray();
        _authRequired = isServer && _accessTokens.Length > 0;
        _tlsSpkiSha256 = tlsSpkiSha256?.ToArray() ?? [];
        _timeProvider = timeProvider;
        _random = random;
        _lastReceived = timeProvider.GetUtcNow();
    }

    public BftHello? Hello => _hello;
    public uint GoAwayLastStreamId => _hasReceivedGoAway ? _goAwayLastStreamId : uint.MaxValue;
    public bool HasReceivedGoAway => _hasReceivedGoAway;
    public uint MaxOpenedStreamId => _maxOpened;
    internal int PendingWindowUpdateCount
    {
        get { lock (_gate) return _controlFrames.Count(frame => frame.Type == BftFrameType.WindowUpdate); }
    }
    internal ulong OutgoingMessageCount => _session.WriteNoiseCounter;
    internal ulong IncomingMessageCount => _session.ReadNoiseCounter;
    public Task<uint> WaitForGoAwayAsync(CancellationToken cancellationToken) => _goAwayReceived.Task.WaitAsync(cancellationToken);
    internal Task WaitForOutgoingMessageCountAsync(ulong count, CancellationToken cancellationToken) => _session.WaitForWriteCounterAsync(count, cancellationToken);
    internal Task WaitForIncomingMessageCountAsync(ulong count, CancellationToken cancellationToken) => _session.WaitForReadCounterAsync(count, cancellationToken);
    internal Task WaitForClosedAsync(CancellationToken cancellationToken) => _closed.Task.WaitAsync(cancellationToken);
    public Task Completion => _closed.Task;

    public async Task WaitForStreamsClosedAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_streams.Count == 0 || _aborted) return;
                changed = _streamsChanged.Task;
            }
            await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StartServerAsync(CancellationToken cancellationToken)
    {
        if (!_authRequired)
        {
            _readyAccepted = true;
            _ready.TrySetResult(true);
            QueueControl(BftFrame.HelloFrame(new BftHello(_proxyId, _initialWindow, (ushort)_maxStreams, _tlsSpkiSha256, _serviceIds.Order().ToArray())));
            StartLoops();
            return;
        }

        StartLoops();
        try
        {
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), _timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await SendGoAwayWithTimeoutAsync(BftFrame.GoAway(_maxOpened, BftGoAwayCode.AuthFailed), cancellationToken).ConfigureAwait(false);
            Abort(new BftProtocolException("AUTH was not received before the deadline."));
            throw new BftAuthenticationException("AUTH was not received before the deadline.");
        }
        if (!await _ready.Task.ConfigureAwait(false))
            throw new BftAuthenticationException("BFT token authentication failed.");
    }

    public async Task StartClientAsync(string? token, CancellationToken cancellationToken)
    {
        StartLoops();
        if (token is not null) QueueControl(BftFrame.Auth(token));
        var accepted = await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!accepted) throw new BftAuthenticationException("BFT token authentication failed.");
        _readyAccepted = true;
    }

    private void StartLoops()
    {
        _readerTask = ReaderLoopAsync();
        _writerTask = WriterLoopAsync();
        _keepAliveTask = KeepAliveLoopAsync();
    }

    public Task<BftTunnelStream> AcceptStreamAsync(CancellationToken cancellationToken) => _acceptedStreams.Reader.ReadAsync(cancellationToken).AsTask();

    public ValueTask<BftTunnelStream> OpenStreamAsync(byte serviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_isServer) throw new InvalidOperationException("Only the client opens BFT streams.");
        if (!_readyAccepted)
            throw new InvalidOperationException("The tunnel handshake is not complete.");
        if (_hello is null || !_hello.ServiceIds.Contains(serviceId))
            throw new BftServiceUnavailableException(serviceId);

        lock (_gate)
        {
            ThrowIfAborted();
            if (_hasReceivedGoAway) throw new BftGoAwayException(_goAwayLastStreamId);
            if (_streams.Count >= _maxStreams) throw new BftStreamLimitException();
            if (_maxOpened == uint.MaxValue) throw new BftProtocolException("BFT stream ID space is exhausted.");
            var id = ++_maxOpened;
            var state = NewStream(id, serviceId);
            _streams.Add(id, state);
            QueueControlLocked(BftFrame.Open(id, serviceId));
            return ValueTask.FromResult(new BftTunnelStream(this, state));
        }
    }

    internal async ValueTask<byte[]?> TakeChunkForStreamAsync(BftMuxStreamState state, CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_gate)
            {
                if (state.Failure is not null) throw new IOException("BFT stream was reset.", state.Failure);
                if (state.Incoming.Count > 0) return state.Incoming.Dequeue();
                if (state.RemoteClosed) return null;
                ThrowIfAborted();
            }
            await state.IncomingSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal void CheckStreamReadable(BftMuxStreamState state)
    {
        lock (_gate)
        {
            if (state.Failure is not null) throw new IOException("BFT stream was reset.", state.Failure);
            ThrowIfAborted();
        }
    }

    internal async ValueTask WriteAsync(BftMuxStreamState state, ReadOnlyMemory<byte> source, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < source.Length)
        {
            int amount;
            lock (_gate)
            {
                ThrowIfAborted();
                if (state.Failure is not null) throw new IOException("BFT stream was reset.", state.Failure);
                if (state.LocalClosed) throw new IOException("BFT stream write side is closed.");
                var pendingCapacity = _initialWindow - state.PendingOutboundBytes;
                amount = (int)Math.Min(Math.Min(state.SendWindow, pendingCapacity), Math.Min(BftFrameCodec.MaxBodyLength, source.Length - offset));
                if (amount > 0)
                {
                    state.SendWindow -= amount;
                    state.PendingOutboundBytes += amount;
                    state.Outgoing.Enqueue(BftFrame.Data(state.Id, source.Span.Slice(offset, amount)));
                    AddToRoundRobinLocked(state);
                    SignalWriter();
                }
            }
            if (amount == 0)
                await state.WindowSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            else
                offset += amount;
        }
    }

    internal void CompleteWrite(BftMuxStreamState state)
    {
        lock (_gate)
        {
            if (state.LocalClosed || state.Reset || _aborted) return;
            state.LocalClosed = true;
            state.Outgoing.Enqueue(BftFrame.Close(state.Id));
            AddToRoundRobinLocked(state);
            CloseIfFinishedLocked(state);
            state.WindowSignal.Release();
        }
    }

    internal void Reset(BftMuxStreamState state, BftResetCode code)
    {
        lock (_gate)
        {
            if (state.Reset || _aborted) return;
            MarkResetLocked(state, new IOException($"BFT stream reset ({code})."), sendReset: true, code);
        }
    }

    internal void DisposeStream(BftMuxStreamState state)
    {
        lock (_gate)
        {
            if (state.Reset || _aborted || !_streams.ContainsKey(state.Id)) return;
            if (state.RemoteClosed && state.BufferedBytes == 0)
            {
                if (!state.LocalClosed)
                {
                    state.LocalClosed = true;
                    state.Outgoing.Enqueue(BftFrame.Close(state.Id));
                    AddToRoundRobinLocked(state);
                    state.WindowSignal.Release();
                }
                CloseIfFinishedLocked(state);
                return;
            }
            MarkResetLocked(state, new IOException("BFT stream was disposed before graceful completion."), true, BftResetCode.Cancel);
        }
    }

    internal void ReportStreamRead(BftMuxStreamState state, int bytesRead)
    {
        lock (_gate)
        {
            state.BufferedBytes -= bytesRead;
            state.ReceiveWindow += bytesRead;
            if (!state.Reset && !_aborted)
            {
                state.PendingWindowUpdateBytes += bytesRead;
                if (!state.RemoteClosed && state.PendingWindowUpdateBytes >= _initialWindow / 4)
                {
                    var increment = (uint)state.PendingWindowUpdateBytes;
                    state.PendingWindowUpdateBytes = 0;
                    QueueControlLocked(BftFrame.WindowUpdate(state.Id, increment));
                }
            }
            CloseIfFinishedLocked(state);
        }
    }

    private async Task ReaderLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var message = await _session.ReadMessageAsync(_stop.Token).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("BFT peer closed the tunnel.");
                lock (_gate) _lastReceived = _timeProvider.GetUtcNow();
                try
                {
                    var frames = BftFrameCodec.Decode(message);
                    if (_isServer && _authRequired && !_ready.Task.IsCompleted && frames.Count > 1)
                    {
                        foreach (var frame in frames) CryptographicOperations.ZeroMemory(frame.Body);
                        await RejectAuthenticationAsync(_stop.Token).ConfigureAwait(false);
                        break;
                    }
                    foreach (var frame in frames)
                    {
                        try { await ProcessFrameAsync(frame, _stop.Token).ConfigureAwait(false); }
                        finally { CryptographicOperations.ZeroMemory(frame.Body); }
                    }
                }
                finally { CryptographicOperations.ZeroMemory(message); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (BftRecordException ex) { Abort(ex); }
        catch (BftProtocolException ex)
        {
            if (_isServer && !_aborted)
            {
                try { await SendGoAwayWithTimeoutAsync(BftFrame.GoAway(_maxOpened, BftGoAwayCode.ProtocolError)).ConfigureAwait(false); }
                catch { }
            }
            Abort(ex);
        }
        catch (Exception ex) { Abort(ex); }
    }

    internal async Task<BftMuxFrameAction> ProcessFrameAsync(BftFrame frame, CancellationToken cancellationToken)
    {
        if (!_ready.Task.IsCompleted)
        {
            if (_isServer)
            {
                if (frame.Type != BftFrameType.Auth || frame.StreamId != 0)
                {
                    await RejectAuthenticationAsync(cancellationToken).ConfigureAwait(false);
                    return BftMuxFrameAction.AuthenticationRejected;
                }
                if (!IsAccessToken(frame.Body))
                {
                    await RejectAuthenticationAsync(cancellationToken).ConfigureAwait(false);
                    return BftMuxFrameAction.AuthenticationRejected;
                }
                QueueControl(BftFrame.HelloFrame(new BftHello(_proxyId, _initialWindow, (ushort)_maxStreams, _tlsSpkiSha256, _serviceIds.Order().ToArray())));
                _readyAccepted = true;
                _ready.TrySetResult(true);
                return BftMuxFrameAction.Processed;
            }

            if (frame.Type == BftFrameType.GoAway)
            {
                SetGoAway(frame);
                _ready.TrySetResult(false);
                Abort(new BftAuthenticationException("Exit rejected the tunnel."));
                return BftMuxFrameAction.AuthenticationRejected;
            }
            if (frame.Type != BftFrameType.Hello)
                throw new BftProtocolException("HELLO must be the first server frame.");
            _hello = BftFrameCodec.ParseHello(frame.Body);
            _maxStreams = _hello.MaxStreams;
            _initialWindow = _hello.InitialWindow;
            _serviceIds.Clear();
            _serviceIds.UnionWith(_hello.ServiceIds);
            _readyAccepted = true;
            _ready.TrySetResult(true);
            return BftMuxFrameAction.Processed;
        }

        if (_isServer && !_authRequired && frame.Type == BftFrameType.Auth)
            return BftMuxFrameAction.Ignored;

        switch (frame.Type)
        {
            case BftFrameType.Auth:
            case BftFrameType.Hello:
                throw new BftProtocolException("Unexpected AUTH or HELLO frame.");
            case BftFrameType.Open:
                return ProcessOpen(frame);
            case BftFrameType.Data:
            case BftFrameType.WindowUpdate:
            case BftFrameType.Close:
            case BftFrameType.Reset:
                return ProcessStreamFrame(frame);
            case BftFrameType.Ping:
                QueueControl(BftFrame.Pong(frame.Body));
                return BftMuxFrameAction.Processed;
            case BftFrameType.Pong:
                lock (_gate)
                {
                    if (_pendingPing is not null && CryptographicOperations.FixedTimeEquals(_pendingPing, frame.Body))
                    {
                        CryptographicOperations.ZeroMemory(_pendingPing);
                        _pendingPing = null;
                    }
                }
                return BftMuxFrameAction.Processed;
            case BftFrameType.GoAway:
                if (_isServer) throw new BftProtocolException("GOAWAY may only be sent by Exit.");
                SetGoAway(frame);
                return BftMuxFrameAction.Processed;
            default:
                throw new BftProtocolException("Unknown BFT frame type.");
        }
    }

    private async Task RejectAuthenticationAsync(CancellationToken cancellationToken)
    {
        await SendGoAwayWithTimeoutAsync(BftFrame.GoAway(_maxOpened, BftGoAwayCode.AuthFailed), cancellationToken).ConfigureAwait(false);
        _ready.TrySetResult(false);
        Abort(new BftAuthenticationException("BFT token authentication failed."));
    }

    private bool IsAccessToken(ReadOnlySpan<byte> token)
        => FixedTimeEqualsAny(token, _accessTokens);

    internal static bool FixedTimeEqualsAny(ReadOnlySpan<byte> token, IEnumerable<byte[]> candidates)
    {
        var matched = false;
        foreach (var allowed in candidates)
            matched |= CryptographicOperations.FixedTimeEquals(token, allowed);
        return matched;
    }

    private BftMuxFrameAction ProcessOpen(BftFrame frame)
    {
        if (!_isServer) throw new BftProtocolException("OPEN may only be sent by the client.");
        lock (_gate)
        {
            if (_sentGoAway && frame.StreamId > _goAwayLastStreamId) return BftMuxFrameAction.Ignored;
            if (_maxOpened == uint.MaxValue || frame.StreamId != _maxOpened + 1)
                throw new BftProtocolException("OPEN stream IDs must be strictly increasing by one.");
            _maxOpened = frame.StreamId;
            var serviceId = frame.Body[0];
            if (!_serviceIds.Contains(serviceId))
            {
                QueueControlLocked(BftFrame.Reset(frame.StreamId, BftResetCode.ServiceUnavailable));
                return BftMuxFrameAction.ResetServiceUnavailable;
            }
            if (_streams.Count >= _maxStreams)
            {
                QueueControlLocked(BftFrame.Reset(frame.StreamId, BftResetCode.StreamLimit));
                return BftMuxFrameAction.ResetStreamLimit;
            }
            var state = NewStream(frame.StreamId, serviceId);
            _streams.Add(frame.StreamId, state);
            _acceptedStreams.Writer.TryWrite(new BftTunnelStream(this, state));
            return BftMuxFrameAction.Processed;
        }
    }

    private BftMuxFrameAction ProcessStreamFrame(BftFrame frame)
    {
        lock (_gate)
        {
            if (_isServer && _sentGoAway && frame.StreamId > _goAwayLastStreamId) return BftMuxFrameAction.Ignored;
            if (frame.StreamId > _maxOpened)
                throw new BftProtocolException("Frame references a stream that was never opened.");
            if (!_streams.TryGetValue(frame.StreamId, out var state)) return BftMuxFrameAction.Ignored;
            if (state.Reset) return BftMuxFrameAction.Ignored;

            if (frame.Type == BftFrameType.WindowUpdate)
            {
                if (state.LocalClosed) return BftMuxFrameAction.Ignored;
                var increment = BinaryPrimitives.ReadUInt32BigEndian(frame.Body);
                if (state.SendWindow + increment > int.MaxValue)
                {
                    MarkResetLocked(state, new BftProtocolException("Flow-control window overflow."), true, BftResetCode.FlowControl);
                    return BftMuxFrameAction.ResetFlowControl;
                }
                state.SendWindow += increment;
                state.WindowSignal.Release();
                return BftMuxFrameAction.Processed;
            }

            if (frame.Type == BftFrameType.Reset)
            {
                var code = Enum.IsDefined(typeof(BftResetCode), frame.Body[0]) ? (BftResetCode)frame.Body[0] : BftResetCode.Internal;
                MarkResetLocked(state, new IOException($"Peer reset BFT stream ({code})."), false, code);
                return BftMuxFrameAction.Processed;
            }

            if (state.RemoteClosed)
            {
                MarkResetLocked(state, new IOException("Peer sent data after closing its write side."), true, BftResetCode.StreamClosed);
                return BftMuxFrameAction.ResetStreamClosed;
            }

            if (frame.Type == BftFrameType.Close)
            {
                state.RemoteClosed = true;
                state.IncomingSignal.Release();
                CloseIfFinishedLocked(state);
                return BftMuxFrameAction.Processed;
            }

            if (frame.Body.Length > state.ReceiveWindow)
            {
                MarkResetLocked(state, new BftProtocolException("Peer exceeded the stream flow-control window."), true, BftResetCode.FlowControl);
                return BftMuxFrameAction.ResetFlowControl;
            }
            state.ReceiveWindow -= frame.Body.Length;
            state.BufferedBytes += frame.Body.Length;
            state.Incoming.Enqueue(frame.Body.ToArray());
            state.IncomingSignal.Release();
            return BftMuxFrameAction.Processed;
        }
    }

    private void SetGoAway(BftFrame frame)
    {
        var last = BinaryPrimitives.ReadUInt32BigEndian(frame.Body);
        lock (_gate)
        {
            _hasReceivedGoAway = true;
            _goAwayLastStreamId = last;
            _goAwayReceived.TrySetResult(last);
        }
    }

    private BftMuxStreamState NewStream(uint id, byte serviceId) => new(id, serviceId, _initialWindow);

    private void MarkResetLocked(BftMuxStreamState state, Exception error, bool sendReset, BftResetCode code)
    {
        if (state.Reset) return;
        state.Reset = true;
        state.Failure = error;
        if (sendReset) QueueControlLocked(BftFrame.Reset(state.Id, code));
        if (_streams.Remove(state.Id))
        {
            SignalStreamsChangedLocked();
            foreach (var bytes in state.Incoming) CryptographicOperations.ZeroMemory(bytes);
            state.Incoming.Clear();
            state.BufferedBytes = 0;
            state.IncomingSignal.Release();
            state.WindowSignal.Release();
        }
        foreach (var frame in state.Outgoing) CryptographicOperations.ZeroMemory(frame.Body);
        state.Outgoing.Clear();
        state.PendingOutboundBytes = 0;
    }

    private void CloseIfFinishedLocked(BftMuxStreamState state)
    {
        if (!state.LocalClosed || !state.RemoteClosed || !state.LocalCloseSent || state.BufferedBytes != 0) return;
        if (_streams.Remove(state.Id)) SignalStreamsChangedLocked();
        state.IncomingSignal.Release();
        state.WindowSignal.Release();
    }

    private void QueueControl(BftFrame frame)
    {
        lock (_gate) QueueControlLocked(frame);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SignalStreamsChangedLocked()
    {
        var previous = _streamsChanged;
        _streamsChanged = NewSignal();
        previous.TrySetResult();
    }

    private void QueueControlLocked(BftFrame frame)
    {
        _controlFrames.Enqueue(frame);
        SignalWriter();
    }

    private void AddToRoundRobinLocked(BftMuxStreamState state)
    {
        if (state.InRoundRobin) return;
        state.InRoundRobin = true;
        _roundRobin.Add(state.Id);
        SignalWriter();
    }

    private void SignalWriter()
    {
        try { _writerSignal.Release(); }
        catch (SemaphoreFullException) { }
    }

    private async Task WriterLoopAsync()
    {
        try
        {
            while (true)
            {
                await _writerSignal.WaitAsync(_stop.Token).ConfigureAwait(false);
                while (!_stop.IsCancellationRequested)
                {
                    var message = TakeNextMessage(out var completedData, out var completedCloses);
                    if (message is null) break;
                    var writeGateHeld = false;
                    try
                    {
                        await _writeGate.WaitAsync(_stop.Token).ConfigureAwait(false);
                        writeGateHeld = true;
                        await _session.WriteMessageAsync(message, cancellationToken: _stop.Token).ConfigureAwait(false);
                        MarkClosesSent(completedCloses);
                    }
                    finally
                    {
                        if (writeGateHeld) _writeGate.Release();
                        CryptographicOperations.ZeroMemory(message);
                        ReleasePendingData(completedData);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { Abort(ex); }
    }

    private byte[]? TakeNextMessage(out List<(BftMuxStreamState State, int Length)> completedData, out List<BftMuxStreamState> completedCloses)
    {
        var encoded = new List<byte[]>();
        completedData = [];
        completedCloses = [];
        var length = 0;
        lock (_gate)
        {
            while (_controlFrames.Count > 0)
            {
                var next = _controlFrames.Peek();
                var size = BftFrameCodec.HeaderLength + next.Body.Length;
                if (length + size > BftFrameCodec.MaxFrameLength) break;
                _controlFrames.Dequeue();
                encoded.Add(BftFrameCodec.Encode(next));
                length += size;
                CryptographicOperations.ZeroMemory(next.Body);
            }

            var visits = _roundRobin.Count;
            for (var visited = 0; visited < visits && length < BftFrameCodec.MaxFrameLength; visited++)
            {
                if (_roundRobin.Count == 0) break;
                _roundRobinIndex %= _roundRobin.Count;
                var index = _roundRobinIndex;
                var id = _roundRobin[index];
                if (!_streams.TryGetValue(id, out var state) || state.Reset || state.Outgoing.Count == 0)
                {
                    if (state is not null) state.InRoundRobin = false;
                    _roundRobin.RemoveAt(index);
                    if (_roundRobinIndex >= _roundRobin.Count) _roundRobinIndex = 0;
                    visited--;
                    continue;
                }

                var next = state.Outgoing.Peek();
                var size = BftFrameCodec.HeaderLength + next.Body.Length;
                if (length + size <= BftFrameCodec.MaxFrameLength)
                {
                    state.Outgoing.Dequeue();
                    encoded.Add(BftFrameCodec.Encode(next));
                    length += size;
                    CryptographicOperations.ZeroMemory(next.Body);
                    completedData.Add((state, next.Body.Length));
                    if (next.Type == BftFrameType.Close)
                        completedCloses.Add(state);
                }
                if (state.Outgoing.Count == 0)
                {
                    state.InRoundRobin = false;
                    _roundRobin.RemoveAt(index);
                    if (_roundRobinIndex >= _roundRobin.Count) _roundRobinIndex = 0;
                }
                else
                {
                    _roundRobinIndex = (index + 1) % _roundRobin.Count;
                }
            }
        }

        if (encoded.Count == 0) return null;
        var message = new byte[length];
        var offset = 0;
        foreach (var frame in encoded)
        {
            frame.CopyTo(message, offset);
            offset += frame.Length;
            CryptographicOperations.ZeroMemory(frame);
        }
        return message;
    }

    private void MarkClosesSent(List<BftMuxStreamState> completedCloses)
    {
        if (completedCloses.Count == 0) return;
        lock (_gate)
        {
            foreach (var state in completedCloses)
            {
                state.LocalCloseSent = true;
                CloseIfFinishedLocked(state);
            }
        }
    }

    private void ReleasePendingData(List<(BftMuxStreamState State, int Length)> completedData)
    {
        if (completedData.Count == 0) return;
        lock (_gate)
        {
            foreach (var (state, length) in completedData)
            {
                if (state.PendingOutboundBytes < length) continue;
                state.PendingOutboundBytes -= length;
                if (!state.Reset && !_aborted) state.WindowSignal.Release();
            }
        }
    }

    private async Task SendUrgentFrameAsync(BftFrame frame, CancellationToken cancellationToken)
    {
        var message = BftFrameCodec.Encode(frame);
        var writeGateHeld = false;
        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            writeGateHeld = true;
            await _session.WriteMessageAsync(message, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Abort(ex);
            throw;
        }
        finally
        {
            if (writeGateHeld) _writeGate.Release();
            CryptographicOperations.ZeroMemory(message);
            CryptographicOperations.ZeroMemory(frame.Body);
        }
    }

    private async Task SendGoAwayWithTimeoutAsync(BftFrame frame, CancellationToken cancellationToken = default)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        await SendUrgentFrameAsync(frame, linked.Token).ConfigureAwait(false);
    }

    public async Task SendGoAwayAsync(BftGoAwayCode code, CancellationToken cancellationToken = default)
    {
        if (!_isServer) throw new InvalidOperationException("Only Exit may send GOAWAY.");
        uint last;
        lock (_gate)
        {
            if (_sentGoAway) return;
            _sentGoAway = true;
            _goAwayLastStreamId = _maxOpened;
            last = _goAwayLastStreamId;
        }
        await SendUrgentFrameAsync(BftFrame.GoAway(last, code), cancellationToken).ConfigureAwait(false);
        if (code != BftGoAwayCode.Shutdown) Abort(new IOException($"BFT Exit sent GOAWAY ({code})."));
    }

    public async Task ShutdownAsync(TimeSpan drainTimeout, CancellationToken cancellationToken = default)
    {
        if (drainTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(drainTimeout));
        await SendGoAwayAsync(BftGoAwayCode.Shutdown, cancellationToken).ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(drainTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try { await WaitForStreamsClosedAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested) { }
        Abort(new IOException("BFT Exit shutdown completed."));
    }

    private async Task KeepAliveLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                DateTimeOffset due;
                lock (_gate) due = _lastReceived + KeepAliveIdle;
                var delay = due - _timeProvider.GetUtcNow();
                if (delay > TimeSpan.Zero) await Task.Delay(delay, _timeProvider, _stop.Token).ConfigureAwait(false);
                byte[]? ping = null;
                lock (_gate)
                {
                    if (_timeProvider.GetUtcNow() < _lastReceived + KeepAliveIdle) continue;
                    if (_pendingPing is null)
                    {
                        ping = new byte[8];
                        _random.Fill(ping);
                        _pendingPing = ping.ToArray();
                    }
                }
                if (ping is null)
                {
                    await Task.Delay(KeepAliveTimeout, _timeProvider, _stop.Token).ConfigureAwait(false);
                }
                else
                {
                    QueueControl(BftFrame.Ping(ping));
                    CryptographicOperations.ZeroMemory(ping);
                    await Task.Delay(KeepAliveTimeout, _timeProvider, _stop.Token).ConfigureAwait(false);
                }
                lock (_gate)
                {
                    if (_pendingPing is not null)
                        throw new BftProtocolException("BFT PONG timed out.");
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { Abort(ex); }
    }

    private void ThrowIfAborted()
    {
        if (_aborted) throw new IOException("BFT tunnel is closed.", _failure);
    }

    private void Abort(Exception? error)
    {
        lock (_gate)
        {
            if (_aborted) return;
            _aborted = true;
            _failure = error;
            _ready.TrySetException(error ?? new IOException("BFT tunnel closed."));
            _closed.TrySetResult();
            _acceptedStreams.Writer.TryComplete(error);
            foreach (var frame in _controlFrames) CryptographicOperations.ZeroMemory(frame.Body);
            _controlFrames.Clear();
            foreach (var state in _streams.Values)
            {
                state.Failure ??= error ?? new IOException("BFT tunnel closed.");
                foreach (var bytes in state.Incoming) CryptographicOperations.ZeroMemory(bytes);
                state.Incoming.Clear();
                foreach (var frame in state.Outgoing) CryptographicOperations.ZeroMemory(frame.Body);
                state.Outgoing.Clear();
                state.BufferedBytes = 0;
                state.PendingOutboundBytes = 0;
                state.PendingWindowUpdateBytes = 0;
                state.IncomingSignal.Release();
                state.WindowSignal.Release();
            }
            _streams.Clear();
            _roundRobin.Clear();
            if (_pendingPing is not null)
            {
                CryptographicOperations.ZeroMemory(_pendingPing);
                _pendingPing = null;
            }
            SignalStreamsChangedLocked();
        }
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        SignalWriter();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Abort(new ObjectDisposedException(nameof(BftMuxConnection)));
        var tasks = new[] { _readerTask, _writerTask, _keepAliveTask }.Where(task => task is not null).Cast<Task>();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { }
        foreach (var token in _accessTokens) CryptographicOperations.ZeroMemory(token);
        await _session.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
        _writerSignal.Dispose();
        _writeGate.Dispose();
    }
}

public sealed class BftAuthenticationException(string message) : IOException(message);
public sealed class BftServiceUnavailableException(byte serviceId) : IOException($"BFT service {serviceId} is unavailable.")
{
    public byte ServiceId { get; } = serviceId;
}
public sealed class BftStreamLimitException() : IOException("The BFT tunnel stream limit was reached.");
public sealed class BftGoAwayException(uint lastStreamId) : IOException($"Exit sent GOAWAY at stream {lastStreamId}.")
{
    public uint LastStreamId { get; } = lastStreamId;
}
