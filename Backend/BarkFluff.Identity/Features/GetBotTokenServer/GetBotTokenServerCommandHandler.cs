using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.Bots;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Identity.Features.GetBotTokenServer;

public class GetBotTokenServerCommandHandler : IRequestHandler<GetBotTokenServerCommand, GetBotTokenServerResponse>
{
    private readonly JwtService _jwtService;

    public GetBotTokenServerCommandHandler(JwtService jwtService)
    {
        _jwtService = jwtService;
    }

    public Task<GetBotTokenServerResponse> Handle(
        GetBotTokenServerCommand request,
        CancellationToken cancellationToken)
    {
        if (request.BotUserId <= 0)
            throw new NotValidBotUserIdException();

        if (string.IsNullOrWhiteSpace(request.TokenId))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "token_id обязателен"));
        }

        var token = _jwtService.GenerateBotToken(request.BotUserId, request.TokenId.Trim());
        return Task.FromResult(new GetBotTokenServerResponse { Token = token });
    }
}
