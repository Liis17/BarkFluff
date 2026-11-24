using System.Threading.Channels;
using System.Xml.Linq;
using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Onliner;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using RpcException = Grpc.Core.RpcException;
using Status = Grpc.Core.Status;
using StatusCode = Grpc.Core.StatusCode;
using Microsoft.Extensions.Time.Testing;
using static BarkFluff.Client.WinUI.Tests.MessengerTestDoubles;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class ChatTypingTests
{
    [Fact]
    public async Task DraftTypingUsesWebCadenceAndCancelsOnClearSendAndSwitch()
    {
        using var fixture = new Fixture();
        var vm = await fixture.LoadAsync();
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "a");

        vm.DraftText = "a";
        vm.DraftText = "ab";
        fixture.Clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal([("a", TypingAction.Typing)], fixture.Sent);
        await fixture.AdvanceTimerAsync(1);
        Assert.Equal([("a", TypingAction.Typing), ("a", TypingAction.Typing)], fixture.Sent);
        await fixture.AdvanceTimerAsync(1);
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(2, fixture.Sent.Count);

        vm.DraftText = "abc";
        vm.DraftText = "";
        Assert.Equal(("a", TypingAction.Cancelled), fixture.Sent[^1]);
        vm.DraftText = "send";
        var count = fixture.Sent.Count;
        await fixture.Ui.RunAsync(() => vm.SendCommand.ExecuteAsync(null));
        Assert.Equal(count + 1, fixture.Sent.Count);
        Assert.Equal(("a", TypingAction.Cancelled), fixture.Sent[^1]);

        fixture.Ui.Post(() =>
        {
            vm.DraftText = "switch";
            vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
        });
        Assert.Equal(("a", TypingAction.Cancelled), fixture.Sent[^1]);
        Assert.DoesNotContain(fixture.Sent, call => call.Chat == "b");
    }

    [Fact]
    public async Task SwitchingAndExitingSerializeCancellationBehindInFlightTyping()
    {
        var firstSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture { FirstSend = firstSend.Task };
        fixture.Service.Watch("a");
        fixture.Service.Input(true);
        fixture.Service.Watch("b");
        fixture.Service.Input(true);
        var stopped = fixture.Service.StopAsync();
        Assert.Equal([("a", TypingAction.Typing)], fixture.Sent);
        Assert.False(stopped.IsCompleted);

        firstSend.SetResult();
        await stopped.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([("a", TypingAction.Typing), ("a", TypingAction.Cancelled)], fixture.Sent);
        Assert.All(fixture.Streams, stream => Assert.True(stream.Token.IsCancellationRequested));
    }

    [Fact]
    public async Task FailedSendDoesNotBlockNextChatOrExit()
    {
        using var fixture = new Fixture
        {
            FirstSend = Task.FromException(new InvalidOperationException("Unexpected send failure"))
        };
        fixture.Service.Watch("a");
        fixture.Service.Input(true);
        fixture.Service.Watch("b");
        fixture.Service.Input(true);

        await fixture.Service.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([("a", TypingAction.Typing), ("a", TypingAction.Cancelled),
            ("b", TypingAction.Typing), ("b", TypingAction.Cancelled)], fixture.Sent);
    }

    [Fact]
    public async Task HeaderFiltersEventsAndRestoresPresenceAfterTtlOrCancellation()
    {
        using var fixture = new Fixture();
        var vm = await fixture.LoadAsync();
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "a");
        var stream = fixture.Streams.Single();
        Assert.True(vm.IsPresenceLabelVisible);

        await fixture.EmitAsync(stream, Event("a", 1));
        await fixture.EmitAsync(stream, Event("b", 2));
        Assert.False(vm.IsTypingLabelVisible);

        await fixture.EmitAsync(stream, Event("a", 2));
        Assert.Equal("typing…", vm.TypingLabel);
        Assert.True(vm.IsTypingLabelVisible);
        Assert.False(vm.IsPresenceLabelVisible);
        vm.IsReconnecting = true;
        Assert.False(vm.IsTypingLabelVisible);
        Assert.True(vm.IsChatHeaderReconnectingVisible);
        vm.IsReconnecting = false;

        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsTypingLabelVisible);
        await fixture.EmitAsync(stream, Event("a", 2));
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsTypingLabelVisible);
        await fixture.AdvanceTimerAsync(1);
        Assert.True(vm.IsPresenceLabelVisible);

        await fixture.EmitAsync(stream, Event("a", 2));
        await fixture.EmitAsync(stream, Event("a", 2, TypingAction.Cancelled));
        Assert.False(vm.IsTypingLabelVisible);
        Assert.True(vm.IsPresenceLabelVisible);
    }

    [Fact]
    public async Task SubscriptionFollowsChatAndRefreshAndRetriesAfterDisconnect()
    {
        using var fixture = new Fixture { FirstStreamCloses = true };
        fixture.Service.Watch("a");
        Assert.Equal("a", Assert.Single(fixture.Streams).Chat);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        var retry = await fixture.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("a", retry.Chat);

        fixture.Service.RefreshToken();
        await fixture.RunPostedAsync();
        Assert.True(retry.Token.IsCancellationRequested);
        var refreshed = fixture.Streams.Last();
        Assert.Equal("a", refreshed.Chat);
        Assert.NotSame(retry, refreshed);

        await refreshed.Events.Writer.WriteAsync(Event("a", 2));
        var queuedOldEvent = await fixture.TakePostedAsync();
        fixture.Service.Watch("b");
        queuedOldEvent();
        Assert.Empty(fixture.Service.TypingUserIds);
        Assert.True(refreshed.Token.IsCancellationRequested);
        Assert.Equal("b", fixture.Streams.Last().Chat);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnimplementedStreamStaysDisabledUntilTokenRefresh(bool switchChat)
    {
        using var fixture = new Fixture { Unsupported = true };
        fixture.Service.Watch("a");
        await fixture.RunPostedAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        if (switchChat)
        {
            fixture.Service.Watch("b");
        }
        fixture.Service.Input(true);
        Assert.Empty(fixture.Sent);
        Assert.Single(fixture.Streams);

        fixture.Unsupported = false;
        fixture.Service.RefreshToken();
        await fixture.RunPostedAsync();
        var chat = switchChat ? "b" : "a";
        Assert.Equal(2, fixture.Streams.Count);
        Assert.Equal(chat, fixture.Streams[^1].Chat);
        fixture.Service.Input(true);
        Assert.Equal((chat, TypingAction.Typing), Assert.Single(fixture.Sent));
        await fixture.EmitAsync(fixture.Streams[^1], Event(chat, 2));
        Assert.Equal(2L, Assert.Single(fixture.Service.TypingUserIds));
    }

    [Fact]
    public async Task UnimplementedSendFromBeforeRefreshDoesNotDisableCurrentSubscription()
    {
        var firstSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture { FirstSend = firstSend.Task };
        fixture.Service.Watch("a");
        fixture.Service.Input(true);
        fixture.Service.RefreshToken();
        await fixture.RunPostedAsync();
        var refreshed = fixture.Streams[^1];

        firstSend.SetException(new RpcException(new Status(StatusCode.Unimplemented, "")));
        await fixture.RunPostedAsync();

        Assert.False(refreshed.Token.IsCancellationRequested);
        await fixture.EmitAsync(refreshed, Event("a", 2));
        Assert.Equal(2L, Assert.Single(fixture.Service.TypingUserIds));
    }

    [Fact]
    public async Task PrivateReadOnlyAndEditingDoNotSendAndResetDropsPreviousAccount()
    {
        using var fixture = new Fixture();
        fixture.Messenger.Chats.Add(CreateChat("private", chatType: ChatType.Private));
        fixture.Messenger.Chats.Add(CreateChat("restricted", peerUserId: 3));
        fixture.Messenger.UserProfile = id => Task.FromResult<(BarkFluff.WebApi.Core.ErrorReturner, UserData?)>(
            (new(true), new UserData { Id = id, FirstName = "Alice",
                AccountStatus = id == 3 ? AccountStatus.Blocked : AccountStatus.Active }));
        fixture.Messenger.Messages.Add(CreateMessage(10, "a", 1));
        var vm = await fixture.LoadAsync();
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "private");
        vm.DraftText = "private";
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "restricted");
        Assert.False(vm.SelectedChat.CanSend);
        vm.DraftText = "read-only";
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "a");
        vm.StartEditMessageCommand.Execute(Assert.Single(vm.Messages));
        Assert.True(vm.IsEditing);
        vm.DraftText = "editing";
        Assert.Empty(fixture.Sent);
        Assert.DoesNotContain(fixture.Streams, stream => stream.Chat == "private");

        var last = fixture.Streams.Last();
        vm.CancelEditCommand.Execute(null);
        vm.DraftText = "before logout";
        Assert.Equal(("a", TypingAction.Typing), Assert.Single(fixture.Sent));
        await fixture.EmitAsync(last, Event("a", 2));
        Assert.True(vm.IsTypingLabelVisible);
        await last.Events.Writer.WriteAsync(Event("a", 3));
        var queuedOldEvent = await fixture.TakePostedAsync();
        vm.Reset();
        Assert.Equal(("a", TypingAction.Cancelled), fixture.Sent[^1]);
        fixture.Messenger.CurrentUserId = fixture.UserId = 8;
        fixture.Connection = new object();
        await vm.LoadAsync();
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "a");
        queuedOldEvent();
        Assert.False(vm.IsTypingLabelVisible);
        Assert.True(last.Token.IsCancellationRequested);
    }

    [Theory]
    [InlineData("en", "Alice, someone are typing…", "Someone is typing…")]
    [InlineData("ru", "Alice, кто-то печатают…", "Кто-то печатает…")]
    public async Task GroupHeaderCombinesUnknownParticipantsAndFallsBackAboveThree(
        string language, string mixed, string someone)
    {
        using var fixture = new Fixture();
        fixture.Messenger.UserData = new UserData { Id = 2, FirstName = "Alice Rose" };
        var vm = await fixture.LoadAsync(language);
        vm.SelectedChat = vm.Chats.Single(chat => chat.Id == "b");
        var stream = fixture.Streams.Single();
        await fixture.EmitAsync(stream, Event("b", 0, uuid: "remote-1"));
        await fixture.EmitAsync(stream, Event("b", 0, uuid: "remote-2"));
        Assert.Equal(someone, vm.TypingLabel);
        await fixture.EmitAsync(stream, Event("b", 2));
        Assert.Equal(mixed, vm.TypingLabel);
        await fixture.EmitAsync(stream, Event("b", 4));
        Assert.Equal(someone, vm.TypingLabel);
        await fixture.EmitAsync(stream, Event("b", 0, TypingAction.Cancelled, "remote-2"));
        Assert.Equal(mixed, vm.TypingLabel);
        await fixture.EmitAsync(stream, Event("b", 4, TypingAction.Cancelled));
        Assert.Equal(mixed, vm.TypingLabel);
    }

    private static TypingEvent Event(string chat, long user, TypingAction action = TypingAction.Typing, string uuid = "") =>
        new() { ChatId = chat, UserId = user, Action = action, UserUuid = uuid };

    private sealed class Fixture : IDisposable, IUiDispatcher
    {
        private readonly Channel<Action> _posted = Channel.CreateUnbounded<Action>();
        public readonly InlineUiDispatcher Ui = new(serializeContinuations: true);
        public readonly FakeTimeProvider Clock = new();
        public readonly FakeMessengerService Messenger = new();
        public readonly List<(string Chat, TypingAction Action)> Sent = [];
        public readonly List<Stream> Streams = [];
        public readonly Channel<Stream> Started = Channel.CreateUnbounded<Stream>();
        public readonly ChatTypingService Service;
        public bool FirstStreamCloses { get; init; }
        public bool Unsupported { get; set; }
        public Task FirstSend { get; init; } = Task.CompletedTask;
        public object Connection { get; set; } = new();
        public long UserId { get; set; } = 1;

        public Fixture() => Service = new(this, Clock, () =>
        {
            var connection = Connection;
            var user = UserId;
            return new(connection, user, () => ReferenceEquals(Connection, connection) && UserId == user);
        }, Subscribe, (chat, action, _) =>
        {
            Sent.Add((chat, action));
            return Sent.Count == 1 ? FirstSend : Task.CompletedTask;
        });

        public async Task<MessengerViewModel> LoadAsync(string language = "en")
        {
            Messenger.Chats.Add(CreateChat("a", peerUserId: 2));
            Messenger.Chats.Add(CreateChat("b"));
            var vm = CreateViewModel(Messenger, dispatcher: Ui, typing: Service, localization: new Resources(language));
            await vm.LoadAsync();
            return vm;
        }

        private async IAsyncEnumerable<TypingEvent> Subscribe(string chat,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            var stream = new Stream(chat, token);
            Streams.Add(stream);
            if (Unsupported)
            {
                throw new RpcException(new Status(StatusCode.Unimplemented, ""));
            }
            if (FirstStreamCloses && Streams.Count == 1)
            {
                yield break;
            }
            Started.Writer.TryWrite(stream);
            await foreach (var item in stream.Events.Reader.ReadAllAsync(token))
            {
                yield return item;
            }
        }

        public void Post(Action action) => _posted.Writer.TryWrite(action);
        public Task<Action> TakePostedAsync() => _posted.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public async Task RunPostedAsync() => (await TakePostedAsync())();
        public async Task EmitAsync(Stream stream, TypingEvent item)
        {
            await stream.Events.Writer.WriteAsync(item);
            await RunPostedAsync();
        }
        public async Task AdvanceTimerAsync(int seconds)
        {
            Clock.Advance(TimeSpan.FromSeconds(seconds));
            await RunPostedAsync();
        }
        public void Dispose() => Service.Dispose();
    }

    private sealed record Stream(string Chat, CancellationToken Token)
    {
        public Channel<TypingEvent> Events { get; } = Channel.CreateUnbounded<TypingEvent>();
    }

    private sealed class Resources(string language) : ILocalizationService
    {
        private readonly XDocument _document = Read(language);
        private static readonly XName Key = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

        public string GetString(string resourceKey) =>
            _document.Root!.Elements().SingleOrDefault(node => (string?)node.Attribute(Key) == resourceKey)?.Value
            ?? resourceKey;

        public string ResolveSupportedLanguage(string? requestedLanguage) => language;
        public void Apply(string language) { }

        private static XDocument Read(string language)
        {
            using var stream = typeof(Resources).Assembly.GetManifestResourceStream($"Typing.Strings.{language}.xaml");
            return XDocument.Load(stream!);
        }
    }
}
