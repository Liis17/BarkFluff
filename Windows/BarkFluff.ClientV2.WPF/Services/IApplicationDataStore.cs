using BarkFluff.ClientV2.WPF.Models;

namespace BarkFluff.ClientV2.WPF.Services;

public interface IApplicationDataStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<bool> HasSeenWelcomeAsync(CancellationToken cancellationToken = default);

    Task MarkWelcomeSeenAsync(CancellationToken cancellationToken = default);

    Task<string?> GetLanguageAsync(CancellationToken cancellationToken = default);

    Task SaveLanguageAsync(string language, CancellationToken cancellationToken = default);

    Task<ApplicationThemeMode?> GetThemeAsync(CancellationToken cancellationToken = default);

    Task SaveThemeAsync(ApplicationThemeMode theme, CancellationToken cancellationToken = default);

    Task SaveSelectedNodeAsync(NodeProfile node, CancellationToken cancellationToken = default);

    Task<NodeProfile?> GetSelectedNodeAsync(CancellationToken cancellationToken = default);

    Task SaveNodeServiceConfigurationAsync(NodeConnection connection, CancellationToken cancellationToken = default);

    Task<NodeConnection?> GetNodeServiceConfigurationAsync(CancellationToken cancellationToken = default);

    Task SaveProtectedSessionAsync(byte[] protectedData, CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task<byte[]?> GetProtectedSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);

    Task DeleteProtectedSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
