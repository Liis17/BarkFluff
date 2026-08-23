using BarkFluff.Bots.Infrastructure;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Proto.Bots;
using BarkFluff.Shared.Exceptions.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.GetBotToken;

public class GetBotTokenQueryHandler : IRequestHandler<GetBotTokenQuery, GetBotTokenResponse>
{
    private readonly BotsStorage _botsStorage;
    private readonly BotTokenIssuer _tokenIssuer;

    public GetBotTokenQueryHandler(BotsStorage botsStorage, BotTokenIssuer tokenIssuer)
    {
        _botsStorage = botsStorage;
        _tokenIssuer = tokenIssuer;
    }

    public async Task<GetBotTokenResponse> Handle(
        GetBotTokenQuery request,
        CancellationToken cancellationToken)
    {
        var bot = await _botsStorage.GetById(request.BotId);
        if (bot is null)
            throw new BotNotFoundException();

        if (string.IsNullOrWhiteSpace(bot.TokenId))
        {
            throw new Grpc.Core.RpcException(new Grpc.Core.Status(
                Grpc.Core.StatusCode.FailedPrecondition,
                "У бота отсутствует token_id"));
        }

        var token = await _tokenIssuer.GetCurrentAsync(bot.Id, bot.TokenId, cancellationToken);
        return new GetBotTokenResponse { Token = token };
    }
}
