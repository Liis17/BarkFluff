namespace BarkFluff.Client.Core.Services;

public interface IPrivateChatKeyStore
{
    Task<byte[]?> TryGetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default);

    Task SaveAsync(string nodeAddress, long userId, string chatId, byte[] key, CancellationToken cancellationToken = default);

    Task ForgetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default);
}
