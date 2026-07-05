using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.GetUserContacts;

public class GetUserContactsCommandHandler : IRequestHandler<GetUserContactsCommand, GetUserContactsResponse>
{

    private readonly UsersStorage _usersStorage;
    private readonly ILogger<GetUserContactsCommandHandler> _logger;

    public GetUserContactsCommandHandler(UsersStorage usersStorage, ILogger<GetUserContactsCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<GetUserContactsResponse> Handle(GetUserContactsCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Получение контактной информации пользователя {UserId}",
            request.UserId
        );

        var user = await _usersStorage.GetById(request.UserId);

        if (user is null)
        {
            _logger.LogWarning("Пользователь {UserId} не найден", request.UserId);
            throw new UserNotFoundException();
        }

        _logger.LogInformation(
            "Контактная информация пользователя {UserId} ({Username}) успешно получена",
            request.UserId,
            user.Username
        );

        return new GetUserContactsResponse()
        {
            User = user.ToGrpc(),
            Contact = new UserContact()
            {
                Email = user.Contact?.Email ?? string.Empty
            }
        };
    }
}