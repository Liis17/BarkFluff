using BarkFluff.ClientV2.WPF.Models;

namespace BarkFluff.ClientV2.WPF.Services;

public interface ISecureSessionStore
{
    Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default);

    Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
