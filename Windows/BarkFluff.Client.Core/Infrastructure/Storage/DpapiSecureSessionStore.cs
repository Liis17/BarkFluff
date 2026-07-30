using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BarkFluff.Client.Core.Infrastructure.Storage;

public sealed class DpapiSecureSessionStore : ISecureSessionStore
{
    // Строка входит в DPAPI-блоб как entropy, поэтому переименование клиента её не меняет:
    // иначе импортированная из WPF-клиента база перестала бы расшифровываться.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BarkFluff.ClientV2.WPF.session.v1");
    private readonly IApplicationDataStore _dataStore;

    public DpapiSecureSessionStore(IApplicationDataStore dataStore) => _dataStore = dataStore;

    public async Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(session);
        var protectedData = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        await _dataStore.SaveProtectedSessionAsync(protectedData, cancellationToken);
    }

    public async Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var protectedData = await _dataStore.GetProtectedSessionAsync(cancellationToken);
        if (protectedData is null)
        {
            return null;
        }

        try
        {
            var plaintext = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredSession>(plaintext);
        }
        catch (CryptographicException)
        {
            await ClearAsync(cancellationToken);
            return null;
        }
        catch (JsonException)
        {
            await ClearAsync(cancellationToken);
            return null;
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        _dataStore.DeleteProtectedSessionAsync(cancellationToken);
}
