using BarkFluff.Client.Core.Models;

namespace BarkFluff.Client.Core.Services;

public interface ISecureSessionStore
{
    Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default);

    Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
