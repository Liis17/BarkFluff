using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.DeleteBotUser;

/// <summary>
/// Помечает бот-аккаунт удалённым: освобождает username (rename в deleted_{id})
/// и исключает из поиска (IsDraft=true). Чаты бота сохраняются.
/// </summary>
public class DeleteBotUserCommandHandler : IRequestHandler<DeleteBotUserCommand, DeleteBotUserResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<DeleteBotUserCommandHandler> _logger;

    public DeleteBotUserCommandHandler(UsersStorage usersStorage, ILogger<DeleteBotUserCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<DeleteBotUserResponse> Handle(DeleteBotUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _usersStorage.GetById(request.UserId);

        if (user is null || !user.IsBot)
        {
            _logger.LogWarning("Бот {UserId} не найден или не является ботом", request.UserId);
            throw new UserNotFoundException();
        }

        user.Username = $"deleted_{user.Id}";
        user.IsDraft = true;

        await _usersStorage.UpdateTrackedUser(user);

        _logger.LogInformation("Бот-аккаунт {UserId} помечен удалённым, username освобождён", request.UserId);

        return new DeleteBotUserResponse();
    }
}
