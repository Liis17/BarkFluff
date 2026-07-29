using BarkFluff.ClientV2.WPF.Services;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Storage;

public sealed class DpapiPrivateChatKeyStore : IPrivateChatKeyStore
{
    private const string EntropyPrefix = "BarkFluff.ClientV2.WPF.private-chat-key.v1:";
    private readonly IApplicationDataStore _dataStore;
    private readonly ConcurrentDictionary<string, byte[]> _memoryCache = new(StringComparer.Ordinal);

    public DpapiPrivateChatKeyStore(IApplicationDataStore dataStore) => _dataStore = dataStore;

    public async Task<byte[]?> TryGetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default)
    {
        var scope = CreateScope(nodeAddress, userId, chatId);
        if (_memoryCache.TryGetValue(scope, out var cached))
        {
            return cached.ToArray();
        }

        var protectedData = await _dataStore.GetProtectedPrivateChatKeyAsync(scope, cancellationToken);
        if (protectedData is null)
        {
            return null;
        }

        try
        {
            var key = ProtectedData.Unprotect(protectedData, CreateEntropy(scope), DataProtectionScope.CurrentUser);
            _memoryCache[scope] = key;
            return key.ToArray();
        }
        catch (CryptographicException)
        {
            await ForgetAsync(nodeAddress, userId, chatId, cancellationToken);
            return null;
        }
    }

    public async Task SaveAsync(string nodeAddress, long userId, string chatId, byte[] key, CancellationToken cancellationToken = default)
    {
        var scope = CreateScope(nodeAddress, userId, chatId);
        var copy = key.ToArray();
        var protectedData = ProtectedData.Protect(copy, CreateEntropy(scope), DataProtectionScope.CurrentUser);
        await _dataStore.SaveProtectedPrivateChatKeyAsync(scope, protectedData, cancellationToken);
        _memoryCache[scope] = copy;
    }

    public async Task ForgetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default)
    {
        var scope = CreateScope(nodeAddress, userId, chatId);
        _memoryCache.TryRemove(scope, out _);
        await _dataStore.DeleteProtectedPrivateChatKeyAsync(scope, cancellationToken);
    }

    private static string CreateScope(string nodeAddress, long userId, string chatId) =>
        $"{nodeAddress.Trim().TrimEnd('/').ToUpperInvariant()}:{userId}:{chatId}";

    private static byte[] CreateEntropy(string scope) => Encoding.UTF8.GetBytes(EntropyPrefix + scope);
}
