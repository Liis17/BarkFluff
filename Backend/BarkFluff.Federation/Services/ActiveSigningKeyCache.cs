namespace BarkFluff.Federation.Services;

// Синглтон-кеш активного ключа для подписи ИСХОДЯЩИХ S2S-запросов (XFedClientInterceptor):
// каналы/интерсепторы живут дольше одного DI-scope, поэтому ключ не читаем из БД на каждый вызов.
// Обновляется при старте и после RotateSigningKey — тот же триггер, что у WellKnownDocumentService.
public class ActiveSigningKeyCache
{
    public sealed record ActiveKey(string KeyId, byte[] PrivateKeySeed);

    private readonly IServiceScopeFactory _scopeFactory;

    private volatile ActiveKey? _cached;

    public ActiveSigningKeyCache(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public ActiveKey? Current => _cached;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var keyService = scope.ServiceProvider.GetRequiredService<SigningKeyService>();
        var key = await keyService.GetActiveKeyAsync(ct);
        _cached = new ActiveKey(key.KeyId, key.PrivateKeySeed);
    }
}
