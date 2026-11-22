namespace BarkFluff.Client.Core.Services;

public sealed record StickerFrame(int Width, int Height, ReadOnlyMemory<byte> Pixels);

public sealed class StickerFrameCache(
    Func<Uri, CancellationToken, Task<byte[]>> download,
    Func<byte[], int, CancellationToken, Task<StickerFrame?>> decode,
    int byteLimit = 16 * 1024 * 1024)
{
    private readonly record struct Key(Uri Uri, int Size);
    private sealed record Entry(Key Key, StickerFrame Frame);

    private sealed class Download
    {
        public int Users;
        public Task<byte[]>? Task;
    }

    private readonly object _gate = new();
    private readonly SemaphoreSlim _slots = new(4);
    private readonly Dictionary<Key, LinkedListNode<Entry>> _frames = [];
    private readonly LinkedList<Entry> _lru = [];
    private readonly Dictionary<Key, Task<StickerFrame?>> _pending = [];
    private readonly Dictionary<Uri, Download> _downloads = [];
    private CancellationTokenSource? _session;
    private int _bytes;

    public CancellationToken SessionToken
    {
        get
        {
            lock (_gate)
                return _session?.Token ?? new CancellationToken(canceled: true);
        }
    }

    public void StartSession()
    {
        lock (_gate)
            _session ??= new();
    }

    public void EndSession()
    {
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _session;
            _session = null;
            _frames.Clear();
            _lru.Clear();
            _pending.Clear();
            _downloads.Clear();
            _bytes = 0;
        }

        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }
    }

    public Task<StickerFrame?> GetAsync(Uri uri, int size, CancellationToken token = default)
    {
        var key = new Key(uri, size);
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_session is null)
                return Task.FromResult<StickerFrame?>(null);

            if (_frames.TryGetValue(key, out var cached))
            {
                _lru.Remove(cached);
                _lru.AddFirst(cached);
                return Task.FromResult<StickerFrame?>(cached.Value.Frame);
            }

            if (!_pending.TryGetValue(key, out var task))
            {
                if (!_downloads.TryGetValue(uri, out var shared))
                    _downloads.Add(uri, shared = new());

                shared.Users++;
                var session = _session.Token;
                _pending.Add(key, task = Task.Run(() => LoadAsync(key, shared, session)));
            }

            // Отмена одного элемента списка не прерывает загрузку для остальных элементов.
            return task.WaitAsync(token);
        }
    }

    private async Task<StickerFrame?> LoadAsync(Key key, Download shared, CancellationToken session)
    {
        StickerFrame? frame = null;
        try
        {
            await _slots.WaitAsync(session).ConfigureAwait(false);
            try
            {
                Task<byte[]> bytes;
                lock (shared)
                    bytes = shared.Task ??= download(key.Uri, session);

                frame = await decode(await bytes.ConfigureAwait(false), key.Size, session).ConfigureAwait(false);
                session.ThrowIfCancellationRequested();
                return frame;
            }
            finally
            {
                _slots.Release();
            }
        }
        finally
        {
            lock (_gate)
            {
                // Завершившаяся загрузка прошлой сессии не меняет кэш новой.
                if (_session?.Token == session)
                {
                    _pending.Remove(key);
                    if (--shared.Users == 0)
                        _downloads.Remove(key.Uri);
                    if (frame is not null)
                        Remember(key, frame);
                }
            }
        }
    }

    private void Remember(Key key, StickerFrame frame)
    {
        if (frame.Pixels.Length > byteLimit)
            return;

        while (_bytes > byteLimit - frame.Pixels.Length && _lru.Last is { } oldest)
        {
            _bytes -= oldest.Value.Frame.Pixels.Length;
            _frames.Remove(oldest.Value.Key);
            _lru.RemoveLast();
        }

        _frames.Add(key, _lru.AddFirst(new Entry(key, frame)));
        _bytes += frame.Pixels.Length;
    }
}
