using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Features.CreateSystemBot;
using BarkFluff.Bots.Persistence.Services;

using MediatR;

namespace BarkFluff.Bots.Services;

/// <summary>
/// Сидинг системных ботов при старте сервиса (после Migrate) + первичная загрузка BotRegistryCache.
/// Идемпотентен: CreateBotUser в Users идемпотентен, наличие роли проверяется по кэшу.
/// </summary>
public class SystemBotsSeeder
{
    private readonly BotsStorage _botsStorage;
    private readonly BotRegistryCache _registryCache;
    private readonly IMediator _mediator;
    private readonly ILogger<SystemBotsSeeder> _logger;

    public SystemBotsSeeder(
        BotsStorage botsStorage,
        BotRegistryCache registryCache,
        IMediator mediator,
        ILogger<SystemBotsSeeder> logger)
    {
        _botsStorage = botsStorage;
        _registryCache = registryCache;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        _registryCache.Load(await _botsStorage.GetAll());

        // botfather без суффикса "bot" в конце — bypass правил username
        await EnsureSystemBot(SystemBotRole.BotFather, "botfather", "BotFather", bypassUsernameRules: true);
        await EnsureSystemBot(SystemBotRole.LoginNotifier, "barkfluffnotifier", "Barkfluff", bypassUsernameRules: true);
    }

    private async Task EnsureSystemBot(SystemBotRole role, string username, string name, bool bypassUsernameRules)
    {
        if (_registryCache.GetBySystemRole(role) is not null)
        {
            _logger.LogDebug("Системный бот {Role} уже существует", role);
            return;
        }

        var response = await _mediator.Send(new CreateSystemBotCommand
        {
            Username = username,
            Name = name,
            SystemRole = role,
            BypassUsernameRules = bypassUsernameRules,
        });

        _logger.LogInformation("Системный бот {Role} создан: @{Username} (id {BotId})", role, username, response.BotId);
    }
}
