using System.Text.Json;
using System.Text.RegularExpressions;

using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Bots;
using BarkFluff.Shared.Exceptions.Bots;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Bots.Features.SetMyCommands;

public partial class SetMyCommandsCommandHandler : IRequestHandler<SetMyCommandsCommand, SetMyCommandsResponse>
{
    private const int MaxCommands = 100;
    private const int MaxDescriptionLength = 256;

    private readonly BotsStorage _botsStorage;
    private readonly BotRegistryCache _registryCache;
    private readonly MetricsCollector _metrics;

    public SetMyCommandsCommandHandler(
        BotsStorage botsStorage,
        BotRegistryCache registryCache,
        MetricsCollector metrics)
    {
        _botsStorage = botsStorage;
        _registryCache = registryCache;
        _metrics = metrics;
    }

    public async Task<SetMyCommandsResponse> Handle(SetMyCommandsCommand request, CancellationToken cancellationToken)
    {
        _metrics.Increment("bot_api_commands_updated");

        Validate(request.Commands);

        var bot = await _botsStorage.GetById(request.BotId) ?? throw new BotNotFoundException();

        // Пустой список очищает команды
        bot.Commands = request.Commands.Count == 0 ? null : JsonSerializer.Serialize(request.Commands);

        await _botsStorage.Update(bot);
        _registryCache.Set(bot);

        return new SetMyCommandsResponse();
    }

    private static void Validate(List<Domain.BotCommand> commands)
    {
        if (commands.Count > MaxCommands)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Не более {MaxCommands} команд"));

        var seen = new HashSet<string>();

        foreach (var command in commands)
        {
            if (!CommandNamePattern().IsMatch(command.Command))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"Имя команды «{command.Command}» должно состоять из 1–32 символов a-z, 0-9, _"));
            }

            if (!seen.Add(command.Command))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"Команда «{command.Command}» указана дважды"));
            }

            if (string.IsNullOrWhiteSpace(command.Description) || command.Description.Length > MaxDescriptionLength)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"Описание команды «{command.Command}» должно быть от 1 до {MaxDescriptionLength} символов"));
            }
        }
    }

    [GeneratedRegex("^[a-z0-9_]{1,32}$")]
    private static partial Regex CommandNamePattern();
}
