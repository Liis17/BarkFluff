using BarkFluff.Bots.Mapping;
using BarkFluff.Bots.Services;
using BarkFluff.Proto.Bots;
using BarkFluff.Shared.Exceptions.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.GetMe;

public class GetMeQueryHandler : IRequestHandler<GetMeQuery, GetMeResponse>
{
    private readonly BotRegistryCache _registryCache;

    public GetMeQueryHandler(BotRegistryCache registryCache)
    {
        _registryCache = registryCache;
    }

    public Task<GetMeResponse> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        var bot = _registryCache.Get(request.BotId) ?? throw new BotNotFoundException();

        return Task.FromResult(bot.ToGetMeResponse());
    }
}
