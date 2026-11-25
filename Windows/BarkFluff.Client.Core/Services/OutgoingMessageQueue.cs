using System.IO;
using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Files;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Microsoft.Data.Sqlite;

namespace BarkFluff.Client.Core.Services;

public sealed class OutgoingMessageQueue(IOutgoingMessageStore store, IOutgoingMessageTransport transport,
    AppDataPaths paths, TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly OutgoingStaging _staging = new(Path.Combine(paths.DataDirectory, "outgoing"));
    private readonly HashSet<Guid> _copying = [];
    private readonly SemaphoreSlim _slots = new(2);
    private readonly SemaphoreSlim _fileSlots = new(2);
    private Session? _session;
    private volatile QueueSnapshot? _snapshot;

    private sealed record QueueSnapshot(OutgoingScope Scope, OutgoingStatus[] Entries);

    private sealed class Session(OutgoingScope scope, CancellationToken token)
    {
        internal OutgoingScope Scope { get; } = scope;
        internal bool Paused { get; set; }
        internal CancellationToken Token { get; } = token;
        internal List<Entry> Entries { get; } = [];
        internal HashSet<string> ActiveChats { get; } = [];
        internal ITimer? Timer { get; set; }
        internal DateTimeOffset TimerDueAt { get; set; } = DateTimeOffset.MaxValue;
    }

    private sealed class Entry(OutgoingMessage message)
    {
        internal OutgoingMessage Message { get; set; } = message;
        internal CancellationTokenSource? Attempt { get; set; }
        internal OutgoingState State { get; set; } = message.Error is null ? OutgoingState.Waiting : OutgoingState.Failed;
        internal double Progress { get; set; }
        internal int Tries { get; set; }
        internal DateTimeOffset ReadyAt { get; set; }
        internal DateTimeOffset NextProgressUpdate { get; set; }
    }

    internal event Action? Changed;
    internal event Action<OutgoingScope, MessageModel>? Delivered;
    internal Func<OutgoingMessage, Task>? AfterEnqueueCommitAsync { get; init; }

    public Task SweepOrphansAsync() => Task.Run(() =>
    {
        lock (_gate)
        {
            try { _staging.SweepAll(store.ListOperations(), _copying); }
            catch (SqliteException) { } // Без списка записей невозможно безопасно удалить staging.
        }
    });

    internal Task StartAsync(OutgoingScope scope, CancellationToken token = default, bool paused = false) => Task.Run(() =>
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            Stop();
            var session = new Session(scope, token) { Paused = paused };
            try
            {
                session.Entries.AddRange(store.Load(scope).Select(message => new Entry(message)));
                _staging.Sweep(scope, session.Entries.Select(entry => entry.Message.Id).Concat(_copying).ToHashSet());
            }
            catch (SqliteException) { } // Начинаем пустую очередь и сохраняем staging для следующего запуска.
            token.ThrowIfCancellationRequested();
            _session = session;
            Pump(session);
        }
        Changed?.Invoke();
    }, token);

    internal Task StopAsync() => Task.Run(Stop);

    private void Stop()
    {
        lock (_gate)
        {
            if (_session is not { } session)
                return;
            _session = null;
            _snapshot = null;
            session.Timer?.Dispose();
            foreach (var entry in session.Entries)
                entry.Attempt?.Cancel();
        }
    }

    internal Task ClearAsync(OutgoingScope scope) => Task.Run(() =>
    {
        lock (_gate)
        {
            var clearedSession = _session?.Scope == scope ? _session : null;
            if (clearedSession is not null)
                Stop();
            store.Clear(scope);
            clearedSession?.Entries.Clear();
            _staging.Sweep(scope, _copying);
        }
    });

    internal IReadOnlyList<OutgoingStatus> Snapshot(OutgoingScope scope, string chatId) =>
        _snapshot is { } snapshot && snapshot.Scope == scope
            ? snapshot.Entries.Where(entry => entry.Message.ChatId == chatId).ToArray()
            : [];

    internal Task EnqueueAsync(OutgoingScope scope, string chatId, string text, long replyId,
        IReadOnlyList<long>? forwards, IReadOnlyList<string>? attachments,
        IReadOnlyList<(AttachmentSourceFile Source, UploadFileType Type)> files,
        CancellationToken token) => Task.Run(async () =>
    {
        var id = Guid.NewGuid();
        var message = new OutgoingMessage(id, chatId, text, replyId, forwards?.ToArray() ?? [],
            attachments?.ToArray() ?? [], files.Select(file => new OutgoingFile(Guid.NewGuid(),
                file.Source.Name, file.Source.ContentType, file.Type, Path.GetExtension(file.Source.Path))).ToArray(), []);
        Session session;
        lock (_gate)
        {
            session = _session is { } active && active.Scope == scope
                ? active : throw new OperationCanceledException();
            _copying.Add(id);
        }

        var committed = false;
        try
        {
            if (files.Count > 0)
                await _staging.CopyAsync(scope, message, files.Select(file => file.Source).ToArray(), token)
                    .ConfigureAwait(false);
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                if (_session != session)
                    throw new OperationCanceledException();

                store.Save(scope, message);
                committed = true;
                session.Entries.Add(new Entry(message));
                Pump(session);
            }
        }
        finally
        {
            lock (_gate)
            {
                if (!committed)
                    _staging.Remove(scope, id);
                _copying.Remove(id);
            }
        }

        Changed?.Invoke();
        if (AfterEnqueueCommitAsync is { } afterCommit)
            await afterCommit(message).ConfigureAwait(false);
    }, token);

    internal Task<bool> EnqueueNotificationReplyAsync(OutgoingScope scope, NotificationReply reply,
        CancellationToken token) => Task.Run(() =>
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_session is not { } session || session.Scope != scope || session.Token != token)
                throw new OperationCanceledException();

            var message = new OutgoingMessage(reply.OperationId, reply.Target.ChatId, reply.Text,
                0, [], [], [], []);
            if (!store.TrySaveNotificationReply(scope, message))
                return false;
            session.Entries.Add(new Entry(message));
            Pump(session);
        }
        Changed?.Invoke();
        return true;
    }, token);

    internal Task WakeAsync(OutgoingScope scope, CancellationToken token) => Task.Run(() =>
    {
        lock (_gate)
        {
            if (token.IsCancellationRequested || _session is not { } session || session.Scope != scope)
                return;
            session.Paused = false;
            foreach (var entry in session.Entries)
                entry.ReadyAt = default;
            Pump(session);
        }
    });

    internal Task RetryAsync(OutgoingScope scope, Guid id, CancellationToken token = default) => Task.Run(() =>
    {
        lock (_gate)
        {
            if (token.IsCancellationRequested || _session is not { } session || session.Scope != scope || Find(session, id) is not { } entry ||
                entry.State != OutgoingState.Failed)
                return;
            var message = entry.Message with { Error = null };
            store.Save(scope, message);
            entry.Message = message;
            entry.Tries = 0;
            entry.ReadyAt = default;
            entry.State = OutgoingState.Waiting;
            Pump(session);
        }

        Changed?.Invoke();
    });

    internal Task CancelAsync(OutgoingScope scope, Guid id, CancellationToken token = default) => Task.Run(() =>
    {
        lock (_gate)
        {
            if (token.IsCancellationRequested || _session is not { } session || session.Scope != scope || Find(session, id) is not { } entry)
                return;
            Remove(session, entry);
        }

        _staging.Remove(scope, id);
        Changed?.Invoke();
    });

    internal Task<bool> ConfirmAsync(OutgoingScope scope, string chatId, MessageModel message,
        CancellationToken token = default)
    {
        if (message.SenderId != scope.UserId || !Guid.TryParse(message.ClientOperationId, out var id) ||
            _snapshot is not { } snapshot || snapshot.Scope != scope ||
            !snapshot.Entries.Any(entry => entry.Message.Id == id && entry.Message.ChatId == chatId))
            return Task.FromResult(false);

        return Task.Run(() =>
        {
            lock (_gate)
            {
                if (token.IsCancellationRequested || _session is not { } session || session.Scope != scope ||
                    Find(session, id) is not { } entry || entry.Message.ChatId != chatId)
                    return false;
                Remove(session, entry);
            }

            _staging.Remove(scope, id);
            Changed?.Invoke();
            return true;
        });
    }

    private static Entry? Find(Session session, Guid id) =>
        session.Entries.Find(entry => entry.Message.Id == id);

    private void Remove(Session session, Entry entry)
    {
        store.Remove(session.Scope, entry.Message.Id);
        session.Entries.Remove(entry);
        entry.Attempt?.Cancel();
        Pump(session);
    }

    private void Pump(Session session)
    {
        if (_session != session || session.Token.IsCancellationRequested || session.Paused)
            return;

        var next = DateTimeOffset.MaxValue;
        var now = clock.GetUtcNow();
        // Берём только голову каждого чата в порядке записи. Ошибка или backoff
        // головы удерживают последующие сообщения этого чата, сохраняя FIFO.
        foreach (var entry in session.Entries.DistinctBy(entry => entry.Message.ChatId))
        {
            if (session.ActiveChats.Count >= 2)
                break;
            var chatId = entry.Message.ChatId;
            if (entry.State == OutgoingState.Failed || session.ActiveChats.Contains(chatId))
                continue;
            if (entry.ReadyAt > now)
            {
                next = entry.ReadyAt < next ? entry.ReadyAt : next;
                continue;
            }

            var attempt = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
            entry.Attempt = attempt;
            session.ActiveChats.Add(chatId);
            _ = Task.Run(() => AttemptAsync(session, entry, attempt));
        }
        if (next != session.TimerDueAt)
        {
            session.TimerDueAt = next;
            var delay = next == DateTimeOffset.MaxValue ? Timeout.InfiniteTimeSpan : next - now;
            if (session.Timer is { } timer)
                timer.Change(delay, Timeout.InfiniteTimeSpan);
            else
            {
                session.Timer = clock.CreateTimer(_ =>
                {
                    lock (_gate)
                    {
                        session.TimerDueAt = DateTimeOffset.MaxValue;
                        Pump(session);
                    }
                }, null, delay, Timeout.InfiniteTimeSpan);
            }
        }
        UpdateSnapshot(session);
    }

    private async Task AttemptAsync(Session session, Entry entry, CancellationTokenSource attempt)
    {
        var token = attempt.Token;
        try
        {
            await _slots.WaitAsync(token).ConfigureAwait(false);
            try { await DeliverAsync(session, entry, token).ConfigureAwait(false); }
            finally { _slots.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            lock (_gate)
            {
                if (_session == session && !token.IsCancellationRequested)
                    Backoff(entry);
            }
        }
        finally
        {
            bool removed;
            lock (_gate)
            {
                entry.Attempt = null;
                session.ActiveChats.Remove(entry.Message.ChatId);
                attempt.Dispose();
                removed = Find(session, entry.Message.Id) is null;
                Pump(session);
            }
            if (removed)
                _staging.Remove(session.Scope, entry.Message.Id);
            Changed?.Invoke();
        }
    }

    private async Task DeliverAsync(Session session, Entry entry, CancellationToken token)
    {
        var message = entry.Message;
        var scope = session.Scope;
        if (message.Files.Length > 0)
        {
            if (!message.QuotaChecked)
            {
                var available = await GetStorageAvailableAsync(scope, token).ConfigureAwait(false);
                if (available is { } bytes && StagedBytes(scope, message) > bytes)
                {
                    Fail(session, entry, token, new ErrorReturner(false, errorResourceKey: "Messenger_AttachmentStorageFull"));
                    return;
                }

                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    message = message with { QuotaChecked = true };
                    store.Save(scope, message);
                    entry.Message = message;
                }
            }

            var progress = new double[message.Files.Length];
            var remaining = Enumerable.Range(0, message.Files.Length)
                .Where(index => index >= message.Uploaded.Length || message.Uploaded[index] is null).ToArray();
            if (remaining.Length > 0)
            {
                for (var index = 0; index < progress.Length; index++)
                    progress[index] = index < message.Uploaded.Length && message.Uploaded[index] is not null ? 1 : 0;
                PublishState(session, entry, token, OutgoingState.Uploading, progress.Average());
            }

            var results = await Task.WhenAll(remaining.Select(UploadAsync)).ConfigureAwait(false);
            if ((results.FirstOrDefault(IsPermanentFailure) ??
                 results.FirstOrDefault(result => !result.IsSuccess)) is { } error)
            {
                Fail(session, entry, token, error);
                return;
            }
            message = entry.Message;

            async Task<ErrorReturner> UploadAsync(int index)
            {
                var file = message.Files[index];
                var path = _staging.FilePath(scope, message.Id, file);
                var reporter = new UploadProgress(value =>
                {
                    lock (progress)
                    {
                        progress[index] = value;
                        PublishState(session, entry, token, OutgoingState.Uploading, progress.Average());
                    }
                });

                await _fileSlots.WaitAsync(token).ConfigureAwait(false);
                (ErrorReturner error, string? fileId) result;
                try { result = await transport.UploadAsync(scope, file, path, reporter, token).ConfigureAwait(false); }
                finally { _fileSlots.Release(); }
                if (result.error is not { IsSuccess: true } || string.IsNullOrEmpty(result.fileId))
                    return result.error is { IsSuccess: false } error ? error : new ErrorReturner(false);

                lock (_gate)
                {
                    token.ThrowIfCancellationRequested();
                    var current = entry.Message;
                    var uploaded = new string?[current.Files.Length];
                    current.Uploaded.CopyTo(uploaded, 0);
                    uploaded[index] = result.fileId;
                    var updated = current with { Uploaded = uploaded };
                    store.Save(scope, updated);
                    entry.Message = updated;
                }
                return result.error;
            }
        }

        PublishState(session, entry, token, OutgoingState.Sending, 0);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        // Ограничиваем неопределённое ожидание ответа, чтобы освободить слот для других чатов.
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var (sendError, sent) = await transport.SendAsync(scope, message, timeout.Token).ConfigureAwait(false);
        if (sendError is not { IsSuccess: true } || sent is null)
        {
            Fail(session, entry, token, sendError);
            return;
        }

        Action<OutgoingScope, MessageModel>? delivered;
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            Remove(session, entry);
            delivered = Delivered;
        }
        _staging.Remove(scope, message.Id);
        delivered?.Invoke(scope, sent);
    }

    private async Task<long?> GetStorageAvailableAsync(OutgoingScope scope, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        // Проверка квоты предварительная. При медленном ответе решение примет сервер загрузки.
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try { return await transport.GetStorageAvailableAsync(scope, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false); }
        catch (Exception)
        {
            token.ThrowIfCancellationRequested();
            return null;
        }
    }

    private long StagedBytes(OutgoingScope scope, OutgoingMessage message)
    {
        long size = 0;
        foreach (var file in message.Files)
        {
            var path = _staging.FilePath(scope, message.Id, file);
            try { size += new FileInfo(path).Length; }
            catch (IOException) { }
        }
        return size;
    }

    private void PublishState(Session session, Entry entry, CancellationToken token, OutgoingState state, double progress)
    {
        lock (_gate)
        {
            if (_session != session || token.IsCancellationRequested || entry.State == OutgoingState.Sending && state == OutgoingState.Uploading)
                return;
            var now = clock.GetUtcNow();
            if (state == OutgoingState.Uploading && entry.State == state && (entry.Progress == progress ||
                entry.Progress > 0 && now < entry.NextProgressUpdate && progress < 1))
                return;
            entry.NextProgressUpdate = now + TimeSpan.FromMilliseconds(100);
            entry.State = state;
            entry.Progress = progress;
            UpdateSnapshot(session);
        }
        Changed?.Invoke();
    }

    private void UpdateSnapshot(Session session) =>
        _snapshot = new(session.Scope, session.Entries
            .Select(entry => new OutgoingStatus(entry.Message, entry.State, entry.Progress)).ToArray());

    private void Fail(Session session, Entry entry, CancellationToken token, ErrorReturner? error)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            var key = error?.ErrorResourceKey;
            if (IsPermanentFailure(error))
            {
                var failed = entry.Message with { Error = key ?? "Messenger_OutboxRejected" };
                store.Save(session.Scope, failed);
                entry.Message = failed;
                entry.State = OutgoingState.Failed;
            }
            else
            {
                Backoff(entry);
            }
        }
    }

    // Повтор без действий пользователя не восстановит доступ, получателя, файл или квоту.
    private static bool IsPermanentFailure(ErrorReturner? error) =>
        error?.ErrorCode == ErrorReturner.PermanentOutgoingFailure ||
        error?.ErrorResourceKey is "Messenger_UserUnavailable" or "Messenger_AttachmentTooLarge"
            or "Messenger_AttachmentReadFailed" or "Messenger_AttachmentStorageFull";

    private void Backoff(Entry entry)
    {
        entry.State = OutgoingState.Waiting;
        entry.ReadyAt = clock.GetUtcNow() + TimeSpan.FromSeconds(1 << Math.Min(entry.Tries, 6));
        entry.Tries = Math.Min(entry.Tries + 1, 6);
    }

    private sealed class UploadProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

