using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Shared;
using Microsoft.Extensions.Time.Testing;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class AccountSettingsServiceTests
{
    [Fact]
    public async Task LogoutAsync_StopsStreamsBeforeRevokingThenClearsTheLocalSession()
    {
        var calls = new List<string>();
        var realtime = new FakeRealtimeMessengerService { OnStop = () => calls.Add("realtime") };
        var presence = new FakeOnlinePresenceService { OnStop = () => calls.Add("presence") };
        var privateKeys = new FakePrivateChatKeyStore { OnForgetAll = () => calls.Add("keys") };
        using var database = await ChatDraftTests.DraftDatabase.CreateAsync();
        const string node = "https://node.example";
        var store = database.Store;
        store.Save(node, 1, "regular", new("keep", 0, ChatType.Regular));
        store.Save(node, 1, "secret", new("remove", 0, ChatType.Secret));
        store.Save(node, 2, "secret", new("other user", 0, ChatType.Secret));
        store.Save("other-node", 1, "secret", new("other node", 0, ChatType.Secret));
        var messengerService = new FakeMessengerService { CurrentNodeAddress = node };
        messengerService.Chats.Add(MessengerTestDoubles.CreateChat("chat", chatType: ChatType.Private));
        var messenger = MessengerTestDoubles.CreateViewModel(messengerService, realtime, presence,
            timeProvider: new FakeTimeProvider(), draftStore: store, draftRemote: new ChatDraftTests.Remote());
        await messenger.LoadAsync();
        messenger.SelectedChat = Assert.Single(messenger.Chats);
        messenger.DraftText = "pending private write";
        var navigation = new RecordingNavigationService(messenger, calls);
        var service = new TestAccountSettingsService(
            new TestClientSession(),
            realtime,
            presence,
            new RecordingSecureSessionStore(calls),
            privateKeys,
            messenger,
            navigation,
            calls);

        var errorKey = await service.LogoutAsync();

        Assert.Null(errorKey);
        Assert.Equal(["realtime", "presence", "server", "session", "keys", "login"], calls);
        Assert.Equal("regular", Assert.Single(store.Load(node, 1)).Key);
        Assert.Equal("other user", store.Load(node, 2)["secret"].Text);
        Assert.Equal("other node", store.Load("other-node", 1)["secret"].Text);
    }

    private sealed class TestAccountSettingsService : AccountSettingsService
    {
        private readonly List<string> _calls;

        public TestAccountSettingsService(
            IClientSession session,
            IRealtimeMessengerService realtime,
            IOnlinePresenceService presence,
            ISecureSessionStore secureSessionStore,
            IPrivateChatKeyStore privateChatKeyStore,
            MessengerViewModel messengerViewModel,
            IOnboardingNavigationService navigation,
            List<string> calls)
            : base(new BarkFluff.WebApi.Core.WebApi(), session, realtime, presence, secureSessionStore, privateChatKeyStore, messengerViewModel, navigation)
        {
            _calls = calls;
        }

        protected override Task<ErrorReturner> LogoutFromServerAsync(GlobalParam parameters)
        {
            _calls.Add("server");
            return Task.FromResult(new ErrorReturner(true));
        }
    }

    private sealed class TestClientSession : IClientSession
    {
        public NodeConnection? CurrentConnection { get; } = new(
            new NodeProfile("https://node.example", "Node", string.Empty),
            new GlobalParam { UserId = 1 });

        public void SetConnection(NodeConnection connection) { }
    }

    private sealed class RecordingSecureSessionStore(List<string> calls) : ISecureSessionStore
    {
        public Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<StoredSession?>(null);

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("session");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNavigationService(MessengerViewModel messenger, List<string> calls) : IOnboardingNavigationService
    {
        public event EventHandler<OnboardingNavigationEventArgs>? CurrentViewModelChanged
        {
            add { }
            remove { }
        }

        public object? CurrentViewModel => null;

        public void ShowWelcome() { }
        public void ShowSelectNode() { }
        public void ShowConnectedNode() { }
        public void ShowRegistration() { }
        public void ShowPasswordRecovery() { }
        public void ShowMessenger() { }

        public void ShowLogin()
        {
            Assert.Empty(messenger.Chats);
            calls.Add("login");
        }
    }
}
