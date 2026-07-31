using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;

namespace BarkFluff.Client.WinUI.Tests;

/// <summary>
/// Хранилище настроек в памяти для тестов ViewModel.
/// </summary>
internal sealed class TestApplicationDataStore : IApplicationDataStore
{
    private readonly Dictionary<string, string> _preferences = [];
    public WindowClosingBehavior? StoredClosingBehavior { get; set; }

    public int ClosingBehaviorSaveCount { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> HasSeenWelcomeAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task MarkWelcomeSeenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<string?> GetLanguageAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task SaveLanguageAsync(string language, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<ApplicationThemeMode?> GetThemeAsync(CancellationToken cancellationToken = default) => Task.FromResult<ApplicationThemeMode?>(null);
    public Task SaveThemeAsync(ApplicationThemeMode theme, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<WindowClosingBehavior?> GetWindowClosingBehaviorAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(StoredClosingBehavior);

    public Task SaveWindowClosingBehaviorAsync(WindowClosingBehavior behavior, CancellationToken cancellationToken = default)
    {
        StoredClosingBehavior = behavior;
        ClosingBehaviorSaveCount++;
        return Task.CompletedTask;
    }

    public Task SaveSelectedNodeAsync(NodeProfile node, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<NodeProfile?> GetSelectedNodeAsync(CancellationToken cancellationToken = default) => Task.FromResult<NodeProfile?>(null);
    public Task SaveNodeServiceConfigurationAsync(NodeConnection connection, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<NodeConnection?> GetNodeServiceConfigurationAsync(CancellationToken cancellationToken = default) => Task.FromResult<NodeConnection?>(null);

    public Task<string?> GetPreferenceAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_preferences.GetValueOrDefault(key));

    public Task SavePreferenceAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _preferences[key] = value;
        return Task.CompletedTask;
    }
}
