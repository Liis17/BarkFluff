using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Services;

namespace BarkFluff.Bots.Host.Http;

/// <summary>
/// Аутентификация Bot REST API по заголовку X-Bot-Token (токен НЕ в URL — не течёт в логи прокси)
/// + per-bot rate-limit. Кладёт Bot в HttpContext.Items.
/// </summary>
public class BotTokenEndpointFilter : IEndpointFilter
{
    public const string TokenHeader = "X-Bot-Token";
    public const string BotItemKey = "bot";

    private readonly BotTokenAuthenticator _authenticator;
    private readonly BotRateLimiter _rateLimiter;

    public BotTokenEndpointFilter(BotTokenAuthenticator authenticator, BotRateLimiter rateLimiter)
    {
        _authenticator = authenticator;
        _rateLimiter = rateLimiter;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var token = context.HttpContext.Request.Headers[TokenHeader].FirstOrDefault();
        var bot = _authenticator.Authenticate(token);

        if (bot is null)
        {
            return BotApiResponse.Error(StatusCodes.Status401Unauthorized, "Невалидный токен бота");
        }

        if (!_rateLimiter.TryAcquire(bot.Id))
        {
            return BotApiResponse.Error(StatusCodes.Status429TooManyRequests, "Превышен лимит запросов");
        }

        context.HttpContext.Items[BotItemKey] = bot;

        return await next(context);
    }
}

public static class BotHttpContextExtensions
{
    /// <summary>Бот, аутентифицированный BotTokenEndpointFilter.</summary>
    public static Bot GetBot(this HttpContext context)
        => (Bot)context.Items[BotTokenEndpointFilter.BotItemKey]!;
}
