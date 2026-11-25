using System.Collections.Specialized;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Client.Core.ViewModels.Settings;
using BarkFluff.Proto.Users;
using BarkFluff.WebApi.Core.MessengerData;
using Grpc.Core;
using Microsoft.Extensions.Time.Testing;
using System.Reflection;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class ChatFoldersTests
{
    [Fact]
    public async Task FolderTabs_FilterNavigationButKeepSearchGlobalAndChatOpen()
    {
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var work = Folder("work", "a", "b");
        work.SortOrder = 1;
        var home = Folder("home", "a");
        var (state, preferences, _) = CreateState(ui, work, home, Folder("empty"));
        var api = new FakeMessengerService();
        api.Chats.AddRange([
            MessengerTestDoubles.CreateChat("a", "Alpha"),
            MessengerTestDoubles.CreateChat("b", "Beta"),
            MessengerTestDoubles.CreateChat("c", "Outside")
        ]);
        var realtime = new FakeRealtimeMessengerService();
        var vm = MessengerTestDoubles.CreateViewModel(api, realtime, dispatcher: ui, chatFolders: state);
        await ui.RunAsync(vm.LoadAsync);

        ui.Post(() =>
        {
            var all = vm.ChatFolderTabs[0];
            Assert.Equal([null, "empty", "home", "work"], vm.ChatFolderTabs.Select(tab => tab.Id));
            Assert.Same(all, vm.ActiveFolderTab);
            var outside = Assert.Single(vm.VisibleChats);
            Assert.Equal("c", outside.Id);
            vm.SelectedVisibleChat = outside;

            var changes = new List<NotifyCollectionChangedAction>();
            vm.VisibleChats.CollectionChanged += (_, args) => changes.Add(args.Action);
            var workTab = vm.ChatFolderTabs[^1];
            vm.ActiveFolderTab = workTab;
            vm.SelectedVisibleChat = null;
            Assert.Same(outside, vm.SelectedChat);
            Assert.Null(vm.SelectedVisibleChat);
            Assert.Equal(["a", "b"], vm.VisibleChats.Select(chat => chat.Id).Order());

            vm.SelectedVisibleChat = vm.VisibleChats[0];
            var second = vm.VisibleChats[1];
            Assert.True(vm.TryInvokeShortcut(MessengerShortcutKey.CtrlDown, MessengerShortcutFocus.Composer, () => false));
            Assert.Same(second, vm.SelectedChat);
            Assert.False(vm.TryInvokeShortcut(MessengerShortcutKey.CtrlDown, MessengerShortcutFocus.Composer, () => false));
            vm.SearchText = "outside";
            Assert.Same(outside, Assert.Single(vm.ChatSearchResults));
            vm.SearchText = "";
            Assert.Same(workTab, vm.ActiveFolderTab);

            preferences.Folders.Remove(work);
            realtime.SignalFoldersChanged();
            Assert.Same(all, vm.ActiveFolderTab);
            Assert.Same(second, vm.SelectedChat);
            Assert.Contains(outside, vm.VisibleChats);
            Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, changes);
            vm.ActiveFolderTab = vm.ChatFolderTabs.Single(tab => tab.Id == "home");
            vm.Reset();
            Assert.Same(all, Assert.Single(vm.ChatFolderTabs));
            Assert.Same(all, vm.ActiveFolderTab);
        });
    }

    [Fact]
    public async Task FolderTabs_CountMutedUnreadAndIncludeNewRealtimeChats()
    {
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var (state, _, _) = CreateState(ui, Folder("work", "muted", "new"));
        var api = new FakeMessengerService();
        var muted = MessengerTestDoubles.CreateChat("muted");
        muted.Muted = true;
        muted.CountUnread = 100;
        var outside = MessengerTestDoubles.CreateChat("outside");
        outside.CountUnread = 3;
        api.Chats.AddRange([muted, outside]);
        var realtime = new FakeRealtimeMessengerService();
        var clock = new FakeTimeProvider();
        var vm = MessengerTestDoubles.CreateViewModel(api, realtime, dispatcher: ui, chatFolders: state);
        vm.UnreadBadgeClock = clock;
        await ui.RunAsync(vm.LoadAsync);

        ui.Post(() =>
        {
            var work = vm.ChatFolderTabs[1];
            vm.ActiveFolderTab = work;
            var existing = Assert.Single(vm.VisibleChats);
            Assert.True(existing.IsMuted);
            Assert.Equal(100, work.UnreadCount);
            Assert.Equal("99+", work.BadgeText);
            Assert.Empty(vm.ChatFolderTabs[0].BadgeText);
            clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
            Assert.Equal(3, vm.UnreadBadgeCount);

            var added = MessengerTestDoubles.CreateChat("new");
            added.CountUnread = 1;
            api.Chats.Add(added);
            realtime.Raise(new IncomingMessage("new", MessengerTestDoubles.CreateMessage(1, "new", 2)));
            Assert.Equal(["muted", "new"], vm.VisibleChats.Select(chat => chat.Id).Order());
            Assert.Same(existing, vm.VisibleChats.Single(chat => chat.Id == "muted"));
            Assert.Equal(101, work.UnreadCount);
            realtime.Raise(new IncomingMessage("new", MessengerTestDoubles.CreateMessage(2, "new", 2)));
            Assert.Equal(102, work.UnreadCount);
            clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
            Assert.Equal(5, vm.UnreadBadgeCount);

            existing.UnreadCount = 0;
            clock.Advance(MessengerViewModel.UnreadBadgeUpdateInterval);
            Assert.Equal(2, work.UnreadCount);
            vm.Reset();
        });
    }

    [Fact]
    public async Task MenuAndSettings_ModifyMembershipWithoutReplacingOtherChats()
    {
        var (state, api, _) = CreateState(Folder("work", "unknown", "a"), Folder("home"));
        var chats = new FakeMessengerService();
        chats.Chats.Add(MessengerTestDoubles.CreateChat("a", "Alpha"));
        chats.Chats.Add(MessengerTestDoubles.CreateChat("b", "Beta"));
        var messenger = MessengerTestDoubles.CreateViewModel(messenger: chats, chatFolders: state);
        await messenger.LoadAsync();
        var a = messenger.Chats.Single(chat => chat.Id == "a");
        await messenger.SetChatFolderAsync(a, "home", true, state.Lifetime);
        Assert.All(state.Folders, folder => Assert.Contains("a", folder.ChatList));
        await messenger.SetChatFolderAsync(a, "home", false, state.Lifetime);

        var settings = new SettingsChatFoldersViewModel(state, messenger, new StubLocalizationService());
        await settings.ActivateAsync();
        var work = settings.Folders.Single(folder => folder.Id == "work");
        var selection = Assert.IsType<ChatFolderSelection>(settings.CreateSelection(work));
        Assert.True(selection.Includes(a));
        var b = Assert.Single(selection.Search(" beta "));
        selection.SetIncluded(a, false);
        selection.SetIncluded(b, true);

        api.Folders[0].ChatList.Add("added-on-another-device");
        state.Invalidate();
        await settings.ApplySelectionAsync(selection);

        Assert.Equal([("home", "a", true), ("home", "a", false), ("work", "a", false), ("work", "b", true)], api.Writes);
        Assert.Equal(["unknown", "added-on-another-device", "b"], state.Folders[0].ChatList);
        Assert.Equal(4, api.Writes.Count);
        settings.Deactivate();
    }

    [Fact]
    public async Task RenameFolder_DoesNotSendChatListToUsersApi()
    {
        var session = new ClientSession();
        session.SetConnection(Connection("test-node", 1));
        using var webApi = new WebApiClient();
        var users = new FolderRenameClient(Folder("work", "unknown", "added-on-another-device", "b"));
        // Подменяем внутренний gRPC-клиент, чтобы проверить запрос без подключения к ноде.
        typeof(WebApiClient).GetField("UsersAC", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(webApi, users);
        var preferences = new UserPreferencesService(webApi, session);

        var (error, folder) = await preferences.UpdateChatFolderAsync("work", "Renamed", "icon", CancellationToken.None);

        Assert.Null(error);
        Assert.Equal("Renamed", Assert.IsType<ChatFolderData>(folder).FolderName);
        Assert.Equal(["unknown", "added-on-another-device", "b"], folder.ChatList);
        var request = Assert.IsType<UpdateChatFolderRequest>(users.Request);
        Assert.Equal("work", request.FolderId);
        Assert.Equal("icon", request.FolderIcon);
        Assert.False(request.HasChatListUpdate);
        Assert.Empty(request.ChatList);
    }

    [Fact]
    public async Task SignalBurst_CoalescesReadAndDiscardsSnapshotFromBeforeWrite()
    {
        var (state, api, _) = CreateState(Folder("work"));
        var realtime = new FakeRealtimeMessengerService();
        var messenger = MessengerTestDoubles.CreateViewModel(realtime: realtime, chatFolders: state);
        await messenger.LoadAsync();
        Assert.Single(state.Folders);

        var old = new TaskCompletionSource<IReadOnlyList<ChatFolderData>>();
        var latest = new TaskCompletionSource<IReadOnlyList<ChatFolderData>>();
        var requests = 0;
        var secondRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Read = () =>
        {
            if (++requests == 1)
            {
                return old.Task;
            }

            secondRequest.SetResult();
            return latest.Task;
        };
        realtime.SignalFoldersChanged();
        var refresh = state.RefreshAsync();
        await state.SetMembershipAsync("work", "a", true);
        for (var signal = 0; signal < 10; signal++)
        {
            realtime.SignalFoldersChanged();
        }

        Assert.Equal(1, requests);
        old.SetResult([Folder("work")]);
        await secondRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, requests);
        Assert.Contains("a", Assert.Single(state.Folders).ChatList);
        latest.SetResult([Folder("work", "a", "remote")]);
        await refresh;
        Assert.Equal(["a", "remote"], Assert.Single(state.Folders).ChatList);

        // Сверка основного потока тоже обновляет папки, даже без сигнала от их RPC.
        api.Read = null;
        api.Folders[0].ChatList.Add("missed-during-reconnect");
        realtime.Resync();
        Assert.Contains("missed-during-reconnect", Assert.Single(state.Folders).ChatList);
    }

    [Fact]
    public async Task WriteFailure_IgnoresDuplicateMenuToggleButReportsBusyForOtherWrites()
    {
        var (state, api, _) = CreateState(Folder("work"));
        var chats = new FakeMessengerService();
        chats.Chats.Add(MessengerTestDoubles.CreateChat("a", "Alpha"));
        var messenger = MessengerTestDoubles.CreateViewModel(messenger: chats, chatFolders: state);
        await messenger.LoadAsync();
        var reply = new TaskCompletionSource<ChatFolderData?>();
        api.Write = () => reply.Task;
        var a = Assert.Single(messenger.Chats);

        var pending = messenger.SetChatFolderAsync(a, "work", true, state.Lifetime);
        Assert.Contains("a", Assert.Single(state.Folders).ChatList);
        await messenger.SetChatFolderAsync(a, "work", true, state.Lifetime);
        Assert.Null(messenger.FolderActionError);
        Assert.Single(api.Writes);
        var selectionError = await state.ApplySelectionAsync("work", new Dictionary<string, bool> { ["b"] = true });
        reply.SetResult(null);
        await pending;

        Assert.Empty(Assert.Single(state.Folders).ChatList);
        Assert.Equal("Error_SettingsSaveFailed", messenger.FolderActionError);
        Assert.Equal("Error_ChatFoldersSaveInProgress", selectionError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplySelection_PartialFailureShowsErrorAndReloadsServerSnapshot(bool throws)
    {
        var (state, api, _) = CreateState(Folder("work"));
        var chats = new FakeMessengerService();
        chats.Chats.Add(MessengerTestDoubles.CreateChat("a", "Alpha"));
        chats.Chats.Add(MessengerTestDoubles.CreateChat("b", "Beta"));
        var messenger = MessengerTestDoubles.CreateViewModel(messenger: chats, chatFolders: state);
        await messenger.LoadAsync();
        var localization = new StubLocalizationService();
        var settings = new SettingsChatFoldersViewModel(state, messenger, localization);
        await settings.ActivateAsync();
        var selection = Assert.IsType<ChatFolderSelection>(settings.CreateSelection(Assert.Single(settings.Folders)));
        foreach (var chat in selection.Chats)
        {
            selection.SetIncluded(chat, true);
        }

        api.Write = () =>
        {
            if (api.Writes.Count == 1)
            {
                api.Folders[0].ChatList.Add("a");
                return Task.FromResult<ChatFolderData?>(api.Folders[0].Clone());
            }

            api.Folders[0].ChatList.Add("remote");
            return throws ? Task.FromException<ChatFolderData?>(new InvalidOperationException())
                : Task.FromResult<ChatFolderData?>(null);
        };

        await settings.ApplySelectionAsync(selection);

        Assert.Equal([("work", "a", true), ("work", "b", true)], api.Writes);
        Assert.Equal(localization.GetString("Error_ChatFoldersSelectionPartiallySaved"), settings.ErrorMessage);
        Assert.Equal(["a", "remote"], Assert.Single(state.Folders).ChatList);
        settings.Deactivate();
    }

    [Fact]
    public async Task Reset_DiscardsLateWriteAndReadAcrossAccountsAndNodes()
    {
        var (state, api, session) = CreateState(Folder("old"));
        var realtime = new FakeRealtimeMessengerService();
        var messenger = MessengerTestDoubles.CreateViewModel(realtime: realtime, chatFolders: state);
        await messenger.LoadAsync();
        var read = new TaskCompletionSource<IReadOnlyList<ChatFolderData>>();
        var write = new TaskCompletionSource<ChatFolderData?>();
        api.Read = () => read.Task;
        api.Write = () => write.Task;
        var pendingRead = state.RefreshAsync();
        var pendingWrite = state.SetMembershipAsync("old", "a", true);
        var lifetime = state.Lifetime;

        messenger.Reset();
        Assert.True(lifetime.IsCancellationRequested);
        Assert.Empty(state.Folders);
        session.SetConnection(Connection("other-node", 2));
        api.Folders.Clear();
        api.Folders.Add(Folder("new"));
        api.Read = null;
        api.Write = null;
        await messenger.LoadAsync();

        read.SetResult([Folder("old")]);
        write.SetResult(Folder("old", "a"));
        await pendingRead;
        Assert.Null(await pendingWrite);
        Assert.Equal("new", Assert.Single(state.Folders).FolderId);
    }

    private static (ChatFoldersState, Preferences, ClientSession) CreateState(params ChatFolderData[] folders) =>
        CreateState(new InlineUiDispatcher(), folders);

    private static (ChatFoldersState, Preferences, ClientSession) CreateState(
        InlineUiDispatcher ui, params ChatFolderData[] folders)
    {
        var session = new ClientSession();
        session.SetConnection(Connection("test-node", 1));
        var api = new Preferences();
        api.Folders.AddRange(folders);
        return (new ChatFoldersState(api, session, ui), api, session);
    }

    private static NodeConnection Connection(string node, long userId) =>
        new(new NodeProfile(node, "", ""), new GlobalParam { UserId = userId });

    private static ChatFolderData Folder(string id, params string[] chats) =>
        new() { FolderId = id, FolderName = id, ChatList = { chats } };

    private sealed class FolderRenameClient(ChatFolderData folder) : UsersApi.UsersApiClient
    {
        public UpdateChatFolderRequest? Request { get; private set; }

        public override AsyncUnaryCall<UpdateChatFolderResponse> UpdateChatFolderAsync(UpdateChatFolderRequest request,
            Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            Request = request;
            var updated = folder.Clone();
            updated.FolderName = request.FolderName;
            updated.FolderIcon = request.FolderIcon;
            if (request.HasChatListUpdate)
            {
                updated.ChatList.Clear();
                updated.ChatList.Add(request.ChatList);
            }

            return new AsyncUnaryCall<UpdateChatFolderResponse>(
                Task.FromResult(new UpdateChatFolderResponse { Folder = updated }), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }
    }

    private sealed class Preferences : IUserPreferencesService
    {
        public List<ChatFolderData> Folders { get; } = [];
        public List<(string Folder, string Chat, bool Included)> Writes { get; } = [];
        public Func<Task<IReadOnlyList<ChatFolderData>>>? Read { get; set; }
        public Func<Task<ChatFolderData?>>? Write { get; set; }

        public async Task<(string?, IReadOnlyList<ChatFolderData>?)> GetChatFoldersAsync(CancellationToken token) =>
            (null, Read is null ? Folders.Select(folder => folder.Clone()).ToArray() : await Read());

        public async Task<(string?, ChatFolderData?)> SetChatFolderMembershipAsync(
            string folderId, string chatId, bool included, CancellationToken token)
        {
            Writes.Add((folderId, chatId, included));
            if (Write is not null)
            {
                var result = await Write();
                return (result is null ? "Error_SettingsSaveFailed" : null, result);
            }
            var folder = Folders.Single(folder => folder.FolderId == folderId);
            if (included && !folder.ChatList.Contains(chatId))
            {
                folder.ChatList.Add(chatId);
            }
            else if (!included)
            {
                folder.ChatList.Remove(chatId);
            }
            return (null, folder.Clone());
        }

        public Task<(string?, PrivacySettings?)> GetPrivacySettingsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string?> UpdatePrivacySettingsAsync(PrivacySettings settings, CancellationToken token) => throw new NotSupportedException();
        public Task<(string?, UserStorageInfo?)> GetUserStorageInfoAsync(CancellationToken token) => throw new NotSupportedException();
    }
}
