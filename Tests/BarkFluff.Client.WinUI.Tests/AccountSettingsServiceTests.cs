using BarkFluff.Client.Core.Infrastructure.Storage;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Shared;
using Microsoft.Extensions.Time.Testing;
using BarkFluff.WebApi.Core;
using BarkFluff.WebApi.Core.MessengerData;
using static BarkFluff.Client.Core.Infrastructure.Storage.SqliteApplicationDataStore;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class AccountSettingsServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogoutAsync_StopsStreamsAndClearsTheLocalSession(bool offline)
    {
        var calls = new List<string>();
        var realtime = new FakeRealtimeMessengerService { OnStop = () => calls.Add("realtime") };
        var presence = new FakeOnlinePresenceService { OnStop = () => calls.Add("presence") };
        var privateKeys = new FakePrivateChatKeyStore { OnForgetAll = () => calls.Add("keys") };
        using var database = await ChatDraftTests.DraftDatabase.CreateAsync();
        const string node = "https://node.example";
        var store = database.Store;
        var scopes = new[] { SecretScope.Create(node, 1), SecretScope.Create(node, 2), SecretScope.Create("other-node", 1) };
        foreach (var scope in scopes)
        {
            store.CommitSecret(scope, SecretSnapshot.Empty, [1, 2, 3], 1, writer =>
            {
                foreach (var table in Enum.GetValues<SecretTable>())
                    writer.Set(table, "record", "protected");
            });
        }
        store.Save(node, 1, "regular", new("keep", 0, ChatType.Regular));
        store.Save(node, 1, "secret", new("remove", 0, ChatType.Secret));
        store.Save(node, 2, "secret", new("other user", 0, ChatType.Secret));
        store.Save("other-node", 1, "secret", new("other node", 0, ChatType.Secret));
        var messengerService = new FakeMessengerService { CurrentNodeAddress = node };
        messengerService.Chats.Add(MessengerTestDoubles.CreateChat("chat", chatType: ChatType.Private));
        var ui = new InlineUiDispatcher(serializeContinuations: true);
        var messenger = MessengerTestDoubles.CreateViewModel(messengerService, realtime, presence,
            dispatcher: ui, timeProvider: new FakeTimeProvider(), draftStore: store,
            draftRemote: new ChatDraftTests.Remote());
        Task loading = null!;
        ui.Post(() => loading = messenger.LoadAsync());
        await loading;
        ui.Post(() =>
        {
            messenger.SelectedChat = Assert.Single(messenger.Chats);
            messenger.DraftText = "pending private write";
            messenger.IsOffline = offline;
        });
        var navigation = new RecordingNavigationService(messenger, calls);
        var service = new TestAccountSettingsService(
            new TestClientSession(),
            realtime,
            presence,
            new RecordingSecureSessionStore(calls),
            privateKeys,
            messenger,
            navigation,
            calls,
            store);

        Task<string?> logout = null!;
        ui.Post(() => logout = service.LogoutAsync());
        var errorKey = await logout;

        Assert.Null(errorKey);
        Assert.Equal(offline
            ? ["realtime", "presence", "session", "keys", "offline login"]
            : ["realtime", "presence", "server", "session", "keys", "login"], calls);
        Assert.Equal("regular", Assert.Single(store.Load(node, 1)).Key);
        Assert.Equal("other user", store.Load(node, 2)["secret"].Text);
        Assert.Equal("other node", store.Load("other-node", 1)["secret"].Text);
        Assert.Equal(0UL, store.ReadSecretSnapshot(scopes[0]).Generation);
        Assert.Equal(1UL, store.ReadSecretSnapshot(scopes[1]).Generation);
        Assert.Equal(1UL, store.ReadSecretSnapshot(scopes[2]).Generation);
        foreach (var table in Enum.GetValues<SecretTable>())
        {
            Assert.Empty(store.ReadSecrets<string>(scopes[0], table));
            Assert.Single(store.ReadSecrets<string>(scopes[1], table));
            Assert.Single(store.ReadSecrets<string>(scopes[2], table));
        }
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
            List<string> calls,
            SqliteApplicationDataStore store)
            : base(new BarkFluff.WebApi.Core.WebApi(), session, realtime, presence, secureSessionStore, privateChatKeyStore,
                messengerViewModel, navigation, applicationData: store)
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

        public void ShowLoginAfterOfflineLogout()
        {
            Assert.Empty(messenger.Chats);
            Assert.False(messenger.IsOffline);
            calls.Add("offline login");
        }
    }
}
