using BarkFluff.Bots.Services;

using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BarkFluff.Bots.Host;

/// <summary>
/// Сверка bot-JWT с реестром ботов (token-id revocation) + per-bot rate-limit
/// после штатной JWT-валидации XAuth. Вешается только на BotsExternalApiService (AddServiceOptions).
/// </summary>
public class BotAuthInterceptor : Interceptor
{
    private readonly BotAccessValidator _validator;

    public BotAuthInterceptor(BotAccessValidator validator)
    {
        _validator = validator;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateAsync(context);
        return await continuation(request, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateAsync(context);
        await continuation(request, responseStream, context);
    }

    private async Task ValidateAsync(ServerCallContext context)
    {
        switch (await _validator.ValidateAsync(context.GetHttpContext().User))
        {
            case BotAccessStatus.Unauthenticated:
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Невалидный токен бота"));
            case BotAccessStatus.RateLimited:
                throw new RpcException(new Status(StatusCode.ResourceExhausted, "Превышен лимит запросов"));
        }
    }
}
