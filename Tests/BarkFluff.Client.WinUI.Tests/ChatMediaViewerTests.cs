using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Google.Protobuf.WellKnownTypes;
using BarkFluff.Proto.Shared;
using System.Net;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class ChatMediaViewerTests
{
    private const string Expired = "https://example.test/expired";
    private const string Fresh = "https://example.test/fresh";
    private readonly InlineUiDispatcher _ui = new(serializeContinuations: true);
    private readonly FakeMessengerService _api = new();

    [Fact]
    public Task ThrowingUrlResolverShowsMediaError() => _ui.RunAsync(async () =>
    {
        _api.FileUrl = _ => throw new InvalidOperationException();
        var viewer = Create();
        await viewer.OpenAsync("a", Tile(1, url: ""));
        Assert.Equal("Error_MediaOpen", viewer.Error);
        _api.Attachments.Add(Entry(1, MessageAttachmentType.Image));
        await viewer.OpenAsync("a", Tile(1));
        await viewer.RefreshFailedSourceAsync(viewer.Source!);
        Assert.Equal("Error_MediaOpen", viewer.Error);
    });

    [Fact]
    public Task NavigatesAcrossPagesInBothDirectionsAndSkipsOtherAttachments() => _ui.RunAsync(async () =>
    {
        _api.FileUrl = id => Task.FromResult($"https://example.test/{id}");
        for (var id = 65; id >= 16; id--)
            _api.Attachments.Add(Entry(id, MessageAttachmentType.Image, id));
        _api.Attachments.Add(Entry(15, MessageAttachmentType.Image, 15));
        _api.Attachments.Add(Entry(14, MessageAttachmentType.Video, 14));
        _api.Attachments.Add(Entry(13, MessageAttachmentType.Document));
        _api.Attachments.Add(Entry(12, MessageAttachmentType.Gif, 12));
        var viewer = Create();

        await viewer.OpenAsync("a", Tile(15));
        await viewer.NavigateAsync(-1);
        Assert.Equal(16, viewer.Current?.AttachmentId);
        await viewer.NavigateAsync(1);
        Assert.Equal(15, viewer.Current?.AttachmentId);
        await viewer.NavigateAsync(1);
        Assert.Equal(14, viewer.Current?.AttachmentId);
        Assert.True(viewer.Current?.IsVideo);
        await viewer.NavigateAsync(1);
        Assert.Equal(12, viewer.Current?.AttachmentId);
        Assert.Equal("https://example.test/file-12", viewer.Source?.AbsoluteUri);
        Assert.False(viewer.CanGoNext);
        Assert.Equal(
            new[] { MessageAttachmentType.Image, MessageAttachmentType.Video, MessageAttachmentType.Gif },
            _api.AttachmentRequests.Select(request => request.Type).Distinct());
        Assert.Contains(("a", MessageAttachmentType.Image, 50), _api.AttachmentRequests);
    });

    [Fact]
    public Task RefreshesOnceAndReportsErrorOnlyIfPlaybackStillFails() => _ui.RunAsync(async () =>
    {
        var calls = 0;
        _api.FileUrl = _ =>
        {
            calls++;
            return Task.FromResult(Fresh);
        };
        _api.Attachments.Add(Entry(1, MessageAttachmentType.Image));
        var viewer = Create();
        await viewer.OpenAsync("a", Tile(1));
        var expired = viewer.Source!;

        await viewer.RefreshFailedSourceAsync(expired);
        Assert.Equal(Fresh, viewer.Source?.AbsoluteUri);
        Assert.Null(viewer.Error);
        await viewer.RefreshFailedSourceAsync(expired);
        Assert.Equal(Fresh, viewer.Source?.AbsoluteUri);
        await viewer.RefreshFailedSourceAsync(viewer.Source!);
        Assert.Null(viewer.Source);
        Assert.Equal("Error_MediaOpen", viewer.Error);
        Assert.Equal(1, calls);
    });

    [Fact]
    public Task DelayedRefreshCannotReplaceAnotherAttachment() => _ui.RunAsync(async () =>
    {
        var delayed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.FileUrl = id => id == "file-1" ? delayed.Task : Task.FromResult(Fresh);
        _api.Attachments.AddRange([Entry(1, MessageAttachmentType.Image), Entry(2, MessageAttachmentType.Video)]);
        var viewer = Create();
        await viewer.OpenAsync("a", Tile(1));
        var refresh = viewer.RefreshFailedSourceAsync(viewer.Source!);

        await viewer.NavigateAsync(1);
        delayed.SetResult("https://example.test/stale");
        await refresh;

        Assert.Equal(2, viewer.Current?.AttachmentId);
        Assert.Equal(Fresh, viewer.Source?.AbsoluteUri);
        Assert.Null(viewer.Error);
    });

    [Fact]
    public Task CloseCancelsAnInFlightUrlRequest() => _ui.RunAsync(async () =>
    {
        var delayed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.FileUrl = _ => delayed.Task;
        var viewer = Create();
        var open = viewer.OpenAsync("a", Tile(1, url: ""));
        var lifetime = viewer.Lifetime;
        viewer.Close();
        await open;
        delayed.SetResult(Fresh);

        Assert.True(lifetime.IsCancellationRequested);
        Assert.False(viewer.IsOpen);
        Assert.False(viewer.IsLoading);
        Assert.Null(viewer.Source);
        Assert.Null(viewer.Error);
    });

    [Fact]
    public Task PageFailureCanBeRetriedWithoutLosingCurrentMedia() => _ui.RunAsync(async () =>
    {
        _api.Attachments.AddRange([Entry(1, MessageAttachmentType.Image), Entry(2, MessageAttachmentType.Video)]);
        _api.FileUrl = _ => Task.FromResult(Fresh);
        _api.AttachmentsFail = true;
        var viewer = Create();
        await viewer.OpenAsync("a", Tile(1));

        await viewer.NavigateAsync(1);
        Assert.Equal(1, viewer.Current?.AttachmentId);
        Assert.Equal(Expired, viewer.Source?.AbsoluteUri);
        Assert.Equal("Error_AttachmentsLoadFailed", viewer.Error);
        _api.AttachmentsFail = false;

        await viewer.NavigateAsync(1);
        Assert.Equal(2, viewer.Current?.AttachmentId);
        Assert.Null(viewer.Error);
    });

    [Fact]
    public async Task EscapeClosesMediaBeforeProfileAndLeavingMessengerClosesMedia()
    {
        _api.Chats.AddRange([MessengerTestDoubles.CreateChat("a"), MessengerTestDoubles.CreateChat("b")]);
        var vm = MessengerTestDoubles.CreateViewModel(_api, dispatcher: _ui);
        await _ui.RunAsync(vm.LoadAsync);
        await _ui.RunAsync(async () =>
        {
            vm.SelectedChat = vm.Chats[0];
            await vm.OpenMediaAsync(Tile(1));
            vm.IsProfileVisible = true;
            Assert.True(vm.TryInvokeShortcut(MessengerShortcutKey.Escape, MessengerShortcutFocus.Other, () => false));
            Assert.False(vm.MediaViewer.IsOpen);
            Assert.True(vm.IsProfileVisible);
            vm.IsProfileVisible = false;

            await vm.OpenMediaAsync(Tile(1));
            vm.SelectedChat = vm.Chats[1];
            Assert.False(vm.MediaViewer.IsOpen);

            await vm.OpenMediaAsync(Tile(1));
            await vm.LeaveMessengerAsync();
            Assert.False(vm.MediaViewer.IsOpen);

            await vm.OpenMediaAsync(Tile(1));
            vm.Reset();
            Assert.False(vm.MediaViewer.IsOpen);
        });
    }

    [Fact]
    public Task PrivateChatKeepsProfileAttachmentsWithoutOpeningMediaViewer() => _ui.RunAsync(async () =>
    {
        _api.UserData = new UserData { Id = 7, Username = "bob" };
        _api.Chats.Add(MessengerTestDoubles.CreateChat("private", peerUserId: 7, chatType: ChatType.Private));
        _api.Attachments.Add(Entry(1, MessageAttachmentType.Image));
        var vm = MessengerTestDoubles.CreateViewModel(_api, dispatcher: _ui);
        await vm.LoadAsync();
        vm.SelectedChat = Assert.Single(vm.Chats);
        Assert.True(vm.SelectedChat.IsPrivate);
        await vm.OpenPeerProfileCommand.ExecuteAsync(null);
        Assert.Equal("private", vm.Profile.AttachmentChatId);
        Assert.Single(vm.Profile.PhotosTab.Items);

        await vm.OpenMediaAsync(Tile(1));

        Assert.False(vm.MediaViewer.IsOpen);
    });

    [Fact]
    public Task SavingExpiredMediaRetriesAndWritesTheOriginalOnlyOnce() => _ui.RunAsync(async () =>
    {
        using var handler = new DownloadHandler();
        using var http = new HttpClient(handler);
        var viewer = new ChatMediaViewer(_api, new StubLocalizationService(), http);
        _api.FileUrl = _ => Task.FromResult(Fresh);
        await viewer.OpenAsync("a", Tile(1));
        var writes = 0;
        using var output = new MemoryStream();
        await viewer.SaveAsync(viewer.Current!, viewer.Lifetime, async (extension, copy, _) =>
        {
            Assert.Equal(".mp4", extension);
            writes++;
            await copy(output);
            return true;
        });

        Assert.Equal(new[] { Expired, Fresh }, handler.Requests);
        Assert.Equal(1, writes);
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
        Assert.Equal("Messenger_MediaSaved", viewer.SaveStatus);
    });

    [Fact]
    public Task ThrowingPageRequestShowsAttachmentsError() => _ui.RunAsync(async () =>
    {
        _api.AttachmentPage = (_, _, _) => throw new InvalidOperationException();
        var viewer = Create();
        await viewer.OpenAsync("a", Tile(1));
        Assert.Equal("Error_AttachmentsLoadFailed", viewer.Error);
        await viewer.NavigateAsync(1);
        Assert.Equal("Error_AttachmentsLoadFailed", viewer.Error);
        Assert.Equal(1, viewer.Current?.AttachmentId);
        Assert.False(viewer.IsLoading);
    });

    [Fact]
    public Task NewestAttachmentCannotRequestAPreviousPage() => _ui.RunAsync(async () =>
    {
        _api.Attachments.AddRange([Entry(1, MessageAttachmentType.Image), Entry(2, MessageAttachmentType.Video)]);
        var viewer = Create();
        await viewer.OpenAsync("a", Tile(1));
        Assert.False(viewer.CanGoPrevious);
        var requests = _api.AttachmentRequests.Count;

        await viewer.NavigateAsync(-1);

        Assert.Equal(requests, _api.AttachmentRequests.Count);
        Assert.Equal(1, viewer.Current?.AttachmentId);
    });

    [Fact]
    public Task MissingCurrentAttachmentStopsAfterBoundedPageSearch() => _ui.RunAsync(async () =>
    {
        _api.AttachmentPage = (_, type, offset) => type == MessageAttachmentType.Image
            ? Page(int.MaxValue, Entry(offset + 2, type, -offset))
            : Page(0);
        var viewer = Create();

        await viewer.OpenAsync("a", Tile(1));

        Assert.Equal("Error_MediaNotFound", viewer.Error);
        Assert.Equal(60, _api.AttachmentRequests.Count);
        Assert.False(viewer.CanGoPrevious);
        Assert.False(viewer.CanGoNext);
        Assert.Equal(Expired, viewer.Source?.AbsoluteUri);
    });

    [Fact]
    public Task RepeatedAttachmentsAcrossPagesAreSkipped() => _ui.RunAsync(async () =>
    {
        _api.AttachmentPage = (_, type, offset) => type != MessageAttachmentType.Image
            ? Page(0)
            : offset == 0 ? Page(3, Entry(1, type)) : Page(3, Entry(1, type), Entry(2, type));
        _api.FileUrl = _ => Task.FromResult(Fresh);
        var viewer = Create();
        await viewer.OpenAsync("a", Tile(1));

        await viewer.NavigateAsync(1);
        Assert.Equal(2, viewer.Current?.AttachmentId);
        Assert.False(viewer.CanGoNext);
        await viewer.NavigateAsync(-1);
        Assert.Equal(1, viewer.Current?.AttachmentId);
        Assert.False(viewer.CanGoPrevious);
    });

    [Fact]
    public Task CloseDuringPageLoadIgnoresLateResponse() => _ui.RunAsync(async () =>
    {
        var pending = new TaskCompletionSource<(ErrorReturner, List<ChatAttachmentInfo>?, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.AttachmentPage = (_, type, _) => type == MessageAttachmentType.Image ? pending.Task : Page(0);
        var viewer = Create();
        var open = viewer.OpenAsync("a", Tile(1));
        Assert.True(viewer.IsLoading);

        viewer.Close();
        await open;
        pending.SetResult((new(true), [Entry(1, MessageAttachmentType.Image)], 1));

        Assert.False(viewer.IsOpen);
        Assert.False(viewer.IsLoading);
        Assert.Null(viewer.Current);
        Assert.Null(viewer.Error);
    });

    [Fact]
    public async Task SwitchingChatsDuringNavigationKeepsTheNewViewerSession()
    {
        _api.Chats.AddRange([MessengerTestDoubles.CreateChat("a"), MessengerTestDoubles.CreateChat("b")]);
        var vm = MessengerTestDoubles.CreateViewModel(_api, dispatcher: _ui);
        await _ui.RunAsync(vm.LoadAsync);
        await _ui.RunAsync(async () =>
        {
            var pending = new TaskCompletionSource<(ErrorReturner, List<ChatAttachmentInfo>?, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var viewer = vm.MediaViewer;
            vm.SelectedChat = vm.Chats[0];
            var firstChatId = vm.SelectedChat.Id;
            _api.AttachmentPage = (chat, type, offset) => type != MessageAttachmentType.Image
                ? Page(0)
                : chat != firstChatId ? Page(1, Entry(3, type))
                : offset == 0 ? Page(2, Entry(1, type)) : pending.Task;
            await vm.OpenMediaAsync(Tile(1));
            var navigation = viewer.NavigateAsync(1);
            Assert.True(viewer.IsLoading);
            Assert.Contains((firstChatId, MessageAttachmentType.Image, 1), _api.AttachmentRequests);

            vm.SelectedChat = vm.Chats[1];
            Assert.False(viewer.IsOpen);
            await vm.OpenMediaAsync(Tile(3));
            await navigation;
            pending.SetResult((new(true), [Entry(2, MessageAttachmentType.Image)], 2));

            Assert.Equal(3, viewer.Current?.AttachmentId);
            Assert.Null(viewer.Error);
            Assert.False(viewer.CanGoNext);
        });
    }

    [Fact]
    public Task IdleDownloadShowsSaveError() => _ui.RunAsync(async () =>
    {
        using var body = new StalledReadStream();
        using var http = new HttpClient(new ContentHandler(new StreamContent(body)));
        var viewer = new ChatMediaViewer(_api, new StubLocalizationService(), http, TimeSpan.FromMilliseconds(1));
        await viewer.OpenAsync("a", Tile(1));
        using var output = new MemoryStream();

        await viewer.SaveAsync(viewer.Current!, viewer.Lifetime, async (_, copy, _) =>
        {
            await copy(output);
            return true;
        });

        Assert.True(body.ReadStarted);
        Assert.Equal("Error_MediaSave", viewer.SaveStatus);
        Assert.Empty(output.ToArray());
    });

    [Fact]
    public Task CancellingSaveDialogLeavesBodyUnreadAndShowsNoResult() => _ui.RunAsync(async () =>
    {
        using var body = new StalledReadStream();
        using var http = new HttpClient(new ContentHandler(new StreamContent(body)));
        var viewer = new ChatMediaViewer(_api, new StubLocalizationService(), http);
        await viewer.OpenAsync("a", Tile(1));
        var picked = false;

        await viewer.SaveAsync(viewer.Current!, viewer.Lifetime, (_, _, _) =>
        {
            picked = true;
            return Task.FromResult(false);
        });

        Assert.True(picked);
        Assert.False(body.ReadStarted);
        Assert.Null(viewer.SaveStatus);
    });

    private static Task<(ErrorReturner, List<ChatAttachmentInfo>?, int)> Page(int total, params ChatAttachmentInfo[] entries) =>
        Task.FromResult<(ErrorReturner, List<ChatAttachmentInfo>?, int)>((new(true), [.. entries], total));

    private sealed class StalledReadStream : MemoryStream
    {
        public bool ReadStarted { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class ContentHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class DownloadHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new("video/mp4");
            return Task.FromResult(Requests.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = content }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private ChatMediaViewer Create() => new(_api, new StubLocalizationService());

    private static MessageAttachmentItemViewModel Tile(long id, MessageAttachmentType type = MessageAttachmentType.Image, string url = Expired) =>
        new(type, "", url, $"file-{id}", 1024, 0, 0, $"file-{id}", id);

    private static ChatAttachmentInfo Entry(long id, MessageAttachmentType type, long seconds = 0) => new()
    {
        SentAt = new Timestamp { Seconds = seconds },
        AttachmentId = id,
        Attachment = new() { Id = id, Type = type, FileId = $"file-{id}", FileName = $"file-{id}" }
    };
}
