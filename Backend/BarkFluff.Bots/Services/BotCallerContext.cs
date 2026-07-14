using BarkFluff.Bots.Domain;
using BarkFluff.GrpcServer.XAuth;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Бот текущего запроса внешнего API. Валидность гарантируют
/// BotAuthInterceptor / BotAuthEndpointFilter (botId из claim x-user-id).
/// </summary>
public class BotCallerContext
{
    private readonly UserContext _userContext;
    private readonly BotRegistryCache _registryCache;

    public BotCallerContext(UserContext userContext, BotRegistryCache registryCache)
    {
        _userContext = userContext;
        _registryCache = registryCache;
    }

    public Bot Bot => _registryCache.Get(_userContext.UserId)!;
}
