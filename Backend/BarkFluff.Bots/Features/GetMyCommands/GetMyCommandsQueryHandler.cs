using System.Text.Json;

using BarkFluff.Bots.Mapping;
using BarkFluff.Bots.Services;
using BarkFluff.Proto.Bots;
using BarkFluff.Shared.Exceptions.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.GetMyCommands;

public class GetMyCommandsQueryHandler : IRequestHandler<GetMyCommandsQuery, GetMyCommandsResponse>
{
    private readonly BotRegistryCache _registryCache;

    public GetMyCommandsQueryHandler(BotRegistryCache registryCache)
    {
        _registryCache = registryCache;
    }

    public Task<GetMyCommandsResponse> Handle(GetMyCommandsQuery request, CancellationToken cancellationToken)
    {
        var bot = _registryCache.Get(request.BotId) ?? throw new BotNotFoundException();

        var commands = string.IsNullOrEmpty(bot.Commands)
            ? []
            : JsonSerializer.Deserialize<List<Domain.BotCommand>>(bot.Commands) ?? [];

        var response = new GetMyCommandsResponse();
        response.Commands.AddRange(commands.Select(c => c.ToProto()));

        return Task.FromResult(response);
    }
}
