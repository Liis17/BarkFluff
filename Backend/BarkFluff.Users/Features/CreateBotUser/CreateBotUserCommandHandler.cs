using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Users.Services;

using MediatR;

namespace BarkFluff.Users.Features.CreateBotUser;

public class CreateBotUserCommandHandler : IRequestHandler<CreateBotUserCommand, CreateBotUserResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly PrivacyStorage _privacyStorage;
    private readonly ILogger<CreateBotUserCommandHandler> _logger;

    public CreateBotUserCommandHandler(UsersStorage usersStorage, PrivacyStorage privacyStorage, ILogger<CreateBotUserCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _privacyStorage = privacyStorage;
        _logger = logger;
    }

    public async Task<CreateBotUserResponse> Handle(CreateBotUserCommand request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();
        var firstName = request.FirstName?.Trim();

        if (!request.BypassUsernameRules)
        {
            if (!UsernameFormatValidator.IsValid(username))
            {
                _logger.LogWarning("Username бота {Username} имеет недопустимый формат", username);
                throw new UsernameInvalidFormatException();
            }

            if (!UsernameFormatValidator.HasBotSuffix(username))
            {
                _logger.LogWarning("Username бота {Username} обязан заканчиваться на bot", username);
                throw new UsernameInvalidFormatException();
            }
        }

        // Идемпотентность: если username уже занят ботом — вернуть его id; занят человеком — конфликт.
        var existing = await _usersStorage.GetUserByUsername(username);
        if (existing is not null)
        {
            if (existing.IsBot)
            {
                _logger.LogInformation("Бот {Username} уже существует (id {UserId})", username, existing.Id);
                return new CreateBotUserResponse { UserId = existing.Id, AlreadyExisted = true };
            }

            _logger.LogWarning("Username {Username} уже занят обычным пользователем {UserId}", username, existing.Id);
            throw new UsernameExistException();
        }

        var bot = await _usersStorage.CreateBotUser(username, firstName ?? string.Empty);

        // Дефолтные настройки приватности, как у подтверждённого пользователя.
        await _privacyStorage.GetOrCreate(bot.Id);

        _logger.LogInformation("Создан бот-пользователь {Username} (id {UserId})", username, bot.Id);

        return new CreateBotUserResponse { UserId = bot.Id, AlreadyExisted = false };
    }
}
