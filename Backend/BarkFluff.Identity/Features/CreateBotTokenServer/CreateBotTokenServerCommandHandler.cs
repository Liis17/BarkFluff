using BarkFluff.Identity.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.Bots;

using MediatR;

namespace BarkFluff.Identity.Features.CreateBotTokenServer;

public class CreateBotTokenServerCommandHandler : IRequestHandler<CreateBotTokenServerCommand, CreateBotTokenServerResponse>
{
    private readonly JwtService _jwtService;
    private readonly ILogger<CreateBotTokenServerCommandHandler> _logger;

    public CreateBotTokenServerCommandHandler(JwtService jwtService, ILogger<CreateBotTokenServerCommandHandler> logger)
    {
        _jwtService = jwtService;
        _logger = logger;
    }

    public Task<CreateBotTokenServerResponse> Handle(CreateBotTokenServerCommand request, CancellationToken cancellationToken)
    {
        if (request.BotUserId <= 0)
            throw new NotValidBotUserIdException();

        var tokenId = Guid.NewGuid().ToString();
        var token = _jwtService.GenerateBotToken(request.BotUserId, tokenId);

        _logger.LogInformation("Выпущен bot-токен для бота {BotUserId}, tokenId {TokenId}", request.BotUserId, tokenId);

        return Task.FromResult(new CreateBotTokenServerResponse
        {
            Token = token,
            TokenId = tokenId
        });
    }
}
