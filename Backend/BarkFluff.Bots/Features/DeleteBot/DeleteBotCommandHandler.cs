using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Proto.Bots;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.DeleteBot;

/// <summary>
/// Удаляет бота: строка Bots + BotUpdates (каскадом); в Users освобождается username
/// и аккаунт помечается удалённым. Чаты бот↔пользователь сохраняются.
/// </summary>
public class DeleteBotCommandHandler : IRequestHandler<DeleteBotCommand, DeleteBotResponse>
{
    private readonly BotsStorage _botsStorage;
    private readonly BotRegistryCache _registryCache;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<DeleteBotCommandHandler> _logger;

    public DeleteBotCommandHandler(
        BotsStorage botsStorage,
        BotRegistryCache registryCache,
        UsersServerApi.UsersServerApiClient usersClient,
        ILogger<DeleteBotCommandHandler> logger)
    {
        _botsStorage = botsStorage;
        _registryCache = registryCache;
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<DeleteBotResponse> Handle(DeleteBotCommand request, CancellationToken cancellationToken)
    {
        var bot = await _botsStorage.GetById(request.BotId);

        if (bot is null)
        {
            _logger.LogWarning("Бот {BotId} не найден", request.BotId);
            throw new BotNotFoundException();
        }

        // Сначала Users (освобождение username), затем своя БД — если Users упадёт, бот останется целым.
        await _usersClient.DeleteBotUserAsync(new DeleteBotUserRequest { UserId = bot.Id }, cancellationToken: cancellationToken);

        await _botsStorage.Delete(bot.Id);
        _registryCache.Remove(bot.Id);

        _logger.LogInformation("Бот {Username} (id {BotId}) удалён", bot.Username, bot.Id);

        return new DeleteBotResponse();
    }
}
