using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Updates;
using BarkFluff.WebApi.Core;
using Grpc.Core;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class RealtimeMessengerServiceTests
{
    [Fact]
    public async Task PrimaryStream_ReportsConnectionBeforeAnyElement()
    {
        using var api = new WebApiClient();
        await using var service = new RealtimeMessengerService(api, new ClientSession());
        using var lifetime = new CancellationTokenSource();
        var response = new TaskCompletionSource<NewMessageEvent>();
        var states = new List<bool>();
        var resync = new List<bool>();
        service.ConnectionChanged += (_, state) => states.Add(state);
        service.ResyncRequested += (_, immediate) => resync.Add(immediate);
        var listener = service.ListenForNewMessagesAsync(_ => Connect(Read()), () => true, lifetime.Token);

        var beforeElement = states.ToArray();
        var beforeResync = resync.ToArray();
        response.SetResult(new NewMessageEvent());
        await listener.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([true], beforeElement);
        Assert.Equal([false], beforeResync);
        Assert.Equal([false, true], resync);
        Assert.Equal([true], states);

        async IAsyncEnumerable<NewMessageEvent> Read()
        {
            yield return await response.Task;
            lifetime.Cancel();
        }
    }

    [Fact]
    public async Task UnsupportedStream_OnlyAnnouncesInitialOpenAndSuccessfulReopen()
    {
        using var api = new WebApiClient();
        await using var service = new RealtimeMessengerService(api, new ClientSession());
        using var lifetime = new CancellationTokenSource();
        var announcements = 0;
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var received = new List<int>();
        service.ResyncRequested += (_, _) => announcements++;
        await service.ListenAsync(_ => Connect(Read(++attempts)), item =>
        {
            received.Add(item);
            if (item == 2)
            {
                lifetime.Cancel();
            }
        }, () => true, lifetime.Token, delay: (duration, _) =>
        {
            delays.Add(duration);
            return Task.CompletedTask;
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2], received);
        Assert.Equal(3, announcements); // Initial, a working reopen, and a reopen after real EOF.
        Assert.Equal([2d, 4d, 8d, 16d], delays.Where(duration => duration.TotalSeconds >= 2).Select(duration => duration.TotalSeconds));

        static async IAsyncEnumerable<int> Read(int attempt)
        {
            await Task.CompletedTask;
            if (attempt >= 4)
            {
                yield return attempt - 3;
            }
        }
    }

    [Fact]
    public async Task Publication_UsesCurrentSubscriptionsAndRejectsOldSession()
    {
        using var api = new WebApiClient();
        await using var service = new RealtimeMessengerService(api, new ClientSession());
        using var lifetime = new CancellationTokenSource();
        var first = new TaskCompletionSource();
        var next = new TaskCompletionSource();
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = 0;
        var current = new List<long>();
        var isCurrent = true;
        EventHandler<IncomingMessage> unsubscribed = (_, _) => old++;
        service.MessageReceived += unsubscribed;
        var listener = service.ListenForNewMessagesAsync(_ => Connect(Read()), () => isCurrent, lifetime.Token);
        service.MessageReceived -= unsubscribed;
        service.MessageReceived += (_, message) =>
        {
            current.Add(message.Message.MessageId);
            published.TrySetResult();
        };

        first.SetResult();
        await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        isCurrent = false;
        next.SetResult();
        await listener.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, old);
        Assert.Equal([1L], current);

        async IAsyncEnumerable<NewMessageEvent> Read()
        {
            await first.Task;
            yield return Event(1);
            await next.Task;
            yield return Event(2);
            lifetime.Cancel();
        }
    }

    [Fact]
    public async Task FolderStream_OldNodeStopsWithoutRetryOrConnectionIndicator()
    {
        using var api = new WebApiClient();
        await using var service = new RealtimeMessengerService(api, new ClientSession());
        using var lifetime = new CancellationTokenSource();
        var attempts = 0;
        var signals = 0;
        var connection = new List<bool>();
        service.ChatFoldersChanged += (_, _) => signals++;
        service.ConnectionChanged += (_, connected) => connection.Add(connected);

        await service.ListenForChatFoldersAsync(_ =>
        {
            attempts++;
            return Connect(Read());
        }, () => true, lifetime.Token, (delay, _) =>
        {
            if (delay >= TimeSpan.FromSeconds(2))
            {
                lifetime.Cancel();
            }
            return Task.CompletedTask;
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(lifetime.IsCancellationRequested);
        Assert.Equal(1, attempts);
        Assert.Equal(0, signals);
        Assert.Empty(connection);

        static async IAsyncEnumerable<ChatFoldersChangedEvent> Read()
        {
            yield return await Task.FromException<ChatFoldersChangedEvent>(
                new RpcException(new Status(StatusCode.Unimplemented, "")));
        }
    }

    [Fact]
    public async Task FolderStream_SignalsOnEventAndOnSilentReopen()
    {
        using var api = new WebApiClient();
        await using var service = new RealtimeMessengerService(api, new ClientSession());
        using var lifetime = new CancellationTokenSource();
        var attempts = 0;
        var signals = new List<int>();
        var end = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ChatFoldersChanged += (_, _) =>
        {
            signals.Add(attempts);
            if (attempts == 2)
            {
                lifetime.Cancel();
                end.TrySetResult();
            }
        };

        await service.ListenForChatFoldersAsync(_ => Connect(Read(++attempts)), () => !lifetime.IsCancellationRequested,
            lifetime.Token, (_, _) => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2], signals);

        async IAsyncEnumerable<ChatFoldersChangedEvent> Read(int attempt)
        {
            if (attempt == 1)
            {
                yield return new ChatFoldersChangedEvent();
            }
            else
            {
                await end.Task;
            }
        }
    }

    private static NewMessageEvent Event(long id) => new()
    {
        ChatId = "a",
        Message = new Message { Id = id, Content = new MessageContent { Text = "hello" } }
    };

    private static Task<(ErrorReturner, IAsyncEnumerable<T>?)> Connect<T>(IAsyncEnumerable<T> stream) =>
        Task.FromResult<(ErrorReturner, IAsyncEnumerable<T>?)>((new(true), stream));
}
