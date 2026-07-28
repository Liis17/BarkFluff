using BarkFluff.ClientV2.WPF.Models;

namespace BarkFluff.ClientV2.WPF.Services;

public interface IApplicationDataStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<bool> HasSeenWelcomeAsync(CancellationToken cancellationToken = default);

    Task MarkWelcomeSeenAsync(CancellationToken cancellationToken = default);

    Task<string?> GetLanguageAsync(CancellationToken cancellationToken = default);

    Task SaveLanguageAsync(string language, CancellationToken cancellationToken = default);

    Task SaveSelectedNodeAsync(NodeProfile node, CancellationToken cancellationToken = default);

    Task<NodeProfile?> GetSelectedNodeAsync(CancellationToken cancellationToken = default);
}
