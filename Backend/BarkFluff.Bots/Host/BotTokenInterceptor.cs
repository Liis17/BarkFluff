using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Services;

using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BarkFluff.Bots.Host;

/// <summary>
/// Аутентификация внешнего Bot API по метадате x-bot-token + per-bot rate-limit.
/// Вешается только на BotsExternalApiService (AddServiceOptions).
/// </summary>
public class BotTokenInterceptor : Interceptor
{
    public const string TokenHeader = "x-bot-token";
    public const string BotContextKey = "bot";

    private readonly BotTokenAuthenticator _authenticator;
    private readonly BotRateLimiter _rateLimiter;

    public BotTokenInterceptor(BotTokenAuthenticator authenticator, BotRateLimiter rateLimiter)
    {
        _authenticator = authenticator;
        _rateLimiter = rateLimiter;
    }

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Authenticate(context);
        return continuation(request, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authenticate(context);
        return continuation(request, responseStream, context);
    }

    private void Authenticate(ServerCallContext context)
    {
        var token = context.RequestHeaders.GetValue(TokenHeader);
        var bot = _authenticator.Authenticate(token);

        if (bot is null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Невалидный токен бота"));
        }

        if (!_rateLimiter.TryAcquire(bot.Id))
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "Превышен лимит запросов"));
        }

        context.UserState[BotContextKey] = bot;
    }
}

public static class BotServerCallContextExtensions
{
    /// <summary>Бот, аутентифицированный BotTokenInterceptor.</summary>
    public static Bot GetBot(this ServerCallContext context)
        => (Bot)context.UserState[BotTokenInterceptor.BotContextKey];
}
