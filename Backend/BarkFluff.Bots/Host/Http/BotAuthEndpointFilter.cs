using BarkFluff.Bots.Services;

namespace BarkFluff.Bots.Host.Http;

/// <summary>
/// Сверка bot-JWT с реестром ботов (token-id revocation) + per-bot rate-limit
/// после штатной JWT-авторизации (RequireAuthorization(Bot)). Ошибки — в формате Bot API.
/// </summary>
public class BotAuthEndpointFilter : IEndpointFilter
{
    private readonly BotAccessValidator _validator;

    public BotAuthEndpointFilter(BotAccessValidator validator)
    {
        _validator = validator;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        switch (_validator.Validate(context.HttpContext.User))
        {
            case BotAccessStatus.Unauthenticated:
                return BotApiResponse.Error(StatusCodes.Status401Unauthorized, "Невалидный токен бота");
            case BotAccessStatus.RateLimited:
                return BotApiResponse.Error(StatusCodes.Status429TooManyRequests, "Превышен лимит запросов");
        }

        return await next(context);
    }
}
