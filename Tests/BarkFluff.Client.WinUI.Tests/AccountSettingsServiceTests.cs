using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
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
        var messengerService = new FakeMessengerService();
        messengerService.Chats.Add(MessengerTestDoubles.CreateChat("chat"));
        var messenger = MessengerTestDoubles.CreateViewModel(messengerService, realtime, presence);
        await messenger.LoadAsync();
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
            new GlobalParam());

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
