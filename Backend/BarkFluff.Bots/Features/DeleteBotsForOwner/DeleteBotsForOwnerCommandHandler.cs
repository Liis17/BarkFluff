using BarkFluff.Bots.Features.DeleteBot;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.DeleteBotsForOwner;

public sealed class DeleteBotsForOwnerCommandHandler(
    BotsStorage botsStorage,
    IMediator mediator)
    : IRequestHandler<DeleteBotsForOwnerCommand, DeleteBotsForOwnerResponse>
{
    public async Task<DeleteBotsForOwnerResponse> Handle(
        DeleteBotsForOwnerCommand request, CancellationToken cancellationToken)
    {
        var bots = await botsStorage.GetByOwner(request.OwnerUserId);
        foreach (var bot in bots)
            await mediator.Send(new DeleteBotCommand { BotId = bot.Id }, cancellationToken);

        return new DeleteBotsForOwnerResponse();
    }
}
