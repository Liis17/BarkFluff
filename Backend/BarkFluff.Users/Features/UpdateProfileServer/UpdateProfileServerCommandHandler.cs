using BarkFluff.Proto.Users;
using BarkFluff.Users.Infrastructure;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.UpdateProfileServer;

public class UpdateProfileServerCommandHandler : IRequestHandler<UpdateProfileServerCommand, UpdateProfileServerResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly UserInfoQueueSender _userInfoQueueSender;
    private readonly ILogger<UpdateProfileServerCommandHandler> _logger;

    public UpdateProfileServerCommandHandler(
        UsersStorage usersStorage,
        UserInfoQueueSender userInfoQueueSender,
        ILogger<UpdateProfileServerCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _userInfoQueueSender = userInfoQueueSender;
        _logger = logger;
    }

    public async Task<UpdateProfileServerResponse> Handle(UpdateProfileServerCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Обновление профиля пользователя {UserId} (серверный)", request.UserId);

        var current = await _usersStorage.GetById(request.UserId);
        if (current is null)
            throw new InvalidOperationException($"Пользователь {request.UserId} не найден");

        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();
        var username = request.Username?.Trim();
        var bio = request.Bio?.Trim();

        var nameChanged = !string.IsNullOrEmpty(firstName) && (firstName != current.FirstName || lastName != current.LastName);
        var usernameChanged = !string.IsNullOrEmpty(username) && username != current.Username;
        var bioChanged = bio is not null && bio != (current.Bio ?? string.Empty);

        if (nameChanged)
        {
            await _usersStorage.ChangeName(request.UserId, firstName!, lastName ?? string.Empty);
            await _userInfoQueueSender.NameChangedEvent(request.UserId, firstName!, lastName ?? string.Empty);
            _logger.LogInformation("Имя пользователя {UserId} обновлено: {First} {Last}", request.UserId, firstName, lastName);
        }

        if (usernameChanged)
        {
            await _usersStorage.ChangeUsername(request.UserId, username!);
            await _userInfoQueueSender.UsernameChangedEvent(request.UserId, username!);
            _logger.LogInformation("Username пользователя {UserId} обновлён: {Username}", request.UserId, username);
        }

        if (bioChanged)
        {
            await _usersStorage.ChangeBio(request.UserId, bio!);
            await _userInfoQueueSender.UserBioChangedEvent(request.UserId, bio!);
            _logger.LogInformation("Bio пользователя {UserId} обновлено", request.UserId);
        }

        return new UpdateProfileServerResponse();
    }
}
