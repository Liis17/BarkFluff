using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Proto.Bots;
using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Bots.Features.CreateSystemBot;

public class CreateSystemBotCommandHandler : IRequestHandler<CreateSystemBotCommand, CreateSystemBotResponse>
{
    private readonly BotsStorage _botsStorage;
    private readonly BotRegistryCache _registryCache;
    private readonly BotTokenService _tokenService;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<CreateSystemBotCommandHandler> _logger;

    public CreateSystemBotCommandHandler(
        BotsStorage botsStorage,
        BotRegistryCache registryCache,
        BotTokenService tokenService,
        UsersServerApi.UsersServerApiClient usersClient,
        ILogger<CreateSystemBotCommandHandler> logger)
    {
        _botsStorage = botsStorage;
        _registryCache = registryCache;
        _tokenService = tokenService;
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<CreateSystemBotResponse> Handle(CreateSystemBotCommand request, CancellationToken cancellationToken)
    {
        // Формат/суффикс username валидирует Users (UsernameInvalidFormatException / UsernameExistException)
        var userResponse = await _usersClient.CreateBotUserAsync(new CreateBotUserRequest
        {
            Username = request.Username,
            FirstName = request.Name,
            BypassUsernameRules = request.BypassUsernameRules,
        }, cancellationToken: cancellationToken);

        var botId = userResponse.UserId;

        // Идемпотентность: бот-юзер уже существовал и строка Bots есть — вернуть новый токен нельзя
        // без перегенерации, поэтому просто перегенерируем (единственный вызывающий — AdminPanel/сидер).
        var existing = await _botsStorage.GetById(botId);
        var (token, tokenHash) = _tokenService.GenerateToken(botId);

        if (existing is not null)
        {
            existing.TokenHash = tokenHash;
            existing.Name = request.Name;
            await _botsStorage.Update(existing);
            _registryCache.Set(existing);

            _logger.LogInformation("Системный бот {Username} (id {BotId}) уже существовал — токен перегенерирован", request.Username, botId);
            return new CreateSystemBotResponse { BotId = botId, Token = token };
        }

        var bot = new Bot
        {
            Id = botId,
            OwnerUserId = null,
            Username = request.Username,
            Name = request.Name,
            TokenHash = tokenHash,
            SystemRole = request.SystemRole,
            CreatedAt = DateTime.UtcNow,
        };

        await _botsStorage.Add(bot);
        _registryCache.Set(bot);

        _logger.LogInformation("Создан системный бот {Username} (id {BotId}, роль {SystemRole})", request.Username, botId, request.SystemRole);

        return new CreateSystemBotResponse { BotId = botId, Token = token };
    }
}
