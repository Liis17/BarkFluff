namespace BarkFluff.Client.Core.Services;

public interface IPrivateChatKeyStore
{
    Task<byte[]?> TryGetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default);

    Task SaveAsync(string nodeAddress, long userId, string chatId, byte[] key, CancellationToken cancellationToken = default);

    Task ForgetAsync(string nodeAddress, long userId, string chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Стирает ключи всех приватных чатов. Нужен при выходе из аккаунта: иначе на общей машине
    /// переписка остаётся расшифровываемой для следующего вошедшего.
    /// </summary>
    Task ForgetAllAsync(CancellationToken cancellationToken = default);
}
