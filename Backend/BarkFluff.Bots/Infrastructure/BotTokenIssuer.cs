using BarkFluff.Proto.Identity;

namespace BarkFluff.Bots.Infrastructure;

/// <summary>Выпуск bot-JWT через Identity — единственный эмитент токенов платформы.</summary>
public class BotTokenIssuer
{
    private readonly IdentityServerApi.IdentityServerApiClient _identityClient;

    public BotTokenIssuer(IdentityServerApi.IdentityServerApiClient identityClient)
    {
        _identityClient = identityClient;
    }

    /// <summary>Выпустить новый токен. Возвращает (plaintext-JWT, token_id для БД/кэша).</summary>
    public async Task<(string Token, string TokenId)> IssueAsync(long botId, CancellationToken cancellationToken = default)
    {
        var response = await _identityClient.CreateBotTokenServerAsync(
            new CreateBotTokenServerRequest { BotUserId = botId },
            cancellationToken: cancellationToken);

        return (response.Token, response.TokenId);
    }

    /// <summary>Повторно выпустить JWT с текущим token_id без ротации.</summary>
    public async Task<string> GetCurrentAsync(
        long botId,
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        var response = await _identityClient.GetBotTokenServerAsync(
            new GetBotTokenServerRequest
            {
                BotUserId = botId,
                TokenId = tokenId
            },
            cancellationToken: cancellationToken);

        return response.Token;
    }
}
