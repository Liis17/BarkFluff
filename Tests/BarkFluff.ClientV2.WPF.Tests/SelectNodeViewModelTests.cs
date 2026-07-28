using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.ClientV2.WPF.ViewModels;
using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.ClientV2.WPF.Tests;

public sealed class SelectNodeViewModelTests
{
    [Fact]
    public async Task ConnectManualAsync_Success_SavesNodeAndNavigatesToConnectedView()
    {
        var profile = new NodeProfile("https://node.example.com", "Node", "Description");
        var dataStore = new MemoryDataStore();
        var navigation = new TestNavigationService();
        var viewModel = new SelectNodeViewModel(
            new FakeNodeConnectionService(NodeConnectionResult.Success(new NodeConnection(profile, new GlobalParam()))),
            dataStore,
            navigation,
            new TestLocalizationService());
        viewModel.ManualAddress = profile.BeaconAddress;

        await viewModel.ConnectManualCommand.ExecuteAsync(null);

        Assert.Equal(profile, dataStore.Connection!.Profile);
        Assert.True(navigation.ShowLoginCalled);
    }

    private sealed class FakeNodeConnectionService : INodeConnectionService
    {
        private readonly NodeConnectionResult _result;

        public FakeNodeConnectionService(NodeConnectionResult result)
        {
            _result = result;
        }

        public Task<IReadOnlyList<PublicNode>> GetPublicNodesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PublicNode>>([]);

        public Task<NodeConnectionResult> ConnectAsync(string address, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);

        public bool RestoreConnection(NodeConnection connection) => true;
    }

    private sealed class MemoryDataStore : IApplicationDataStore
    {
        public NodeConnection? Connection { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> HasSeenWelcomeAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task MarkWelcomeSeenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetLanguageAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SaveLanguageAsync(string language, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ApplicationThemeMode?> GetThemeAsync(CancellationToken cancellationToken = default) => Task.FromResult<ApplicationThemeMode?>(null);
        public Task SaveThemeAsync(ApplicationThemeMode theme, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<NodeProfile?> GetSelectedNodeAsync(CancellationToken cancellationToken = default) => Task.FromResult(Connection?.Profile);

        public Task SaveSelectedNodeAsync(NodeProfile node, CancellationToken cancellationToken = default)
        {
            Connection = new NodeConnection(node, new GlobalParam());
            return Task.CompletedTask;
        }

        public Task SaveNodeServiceConfigurationAsync(NodeConnection connection, CancellationToken cancellationToken = default)
        {
            Connection = connection;
            return Task.CompletedTask;
        }

        public Task<NodeConnection?> GetNodeServiceConfigurationAsync(CancellationToken cancellationToken = default) => Task.FromResult(Connection);
    }

    private sealed class TestNavigationService : IOnboardingNavigationService
    {
        public object? LastViewModel { get; private set; }

        public object? CurrentViewModel => LastViewModel;

        public bool ShowLoginCalled { get; private set; }

        public event EventHandler<OnboardingNavigationEventArgs>? CurrentViewModelChanged;

        public void ShowWelcome() => Navigate(new object());
        public void ShowSelectNode() => Navigate(new object());
        public void ShowConnectedNode()
        {
            Navigate(new object());
        }
        public void ShowLogin()
        {
            ShowLoginCalled = true;
            Navigate(new object());
        }
        public void ShowRegistration() => Navigate(new object());
        public void ShowPasswordRecovery() => Navigate(new object());

        private void Navigate(object viewModel)
        {
            LastViewModel = viewModel;
            CurrentViewModelChanged?.Invoke(this, new OnboardingNavigationEventArgs(viewModel));
        }
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public string ResolveSupportedLanguage(string? requestedLanguage) => "en";
        public void Apply(string language) { }
        public string GetString(string resourceKey) => resourceKey;
    }
}
