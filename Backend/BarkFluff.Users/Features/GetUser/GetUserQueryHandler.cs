using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.GetUser;

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, GetUserResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly UserContext _userContext;
    private readonly ILogger<GetUserQueryHandler> _logger;

    public GetUserQueryHandler(
        UsersStorage usersStorage,
        PersonalizationStorage personalizationStorage,
        UserContext userContext,
        ILogger<GetUserQueryHandler> logger)
    {
        _usersStorage = usersStorage;
        _personalizationStorage = personalizationStorage;
        _userContext = userContext;
        _logger = logger;
    }

    public async Task<GetUserResponse> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        var userId = request.UserId ?? _userContext.UserId;

        _logger.LogDebug(
            "Получение информации о пользователе {UserId}. Запросил: {RequesterId}",
            userId,
            _userContext.UserId
        );

        var user = await _usersStorage.GetById(userId);

        if (user == null)
        {
            _logger.LogWarning(
                "Пользователь {UserId} не найден",
                userId
            );
            throw new UserNotFoundException();
        }

        _logger.LogInformation(
            "Информация о пользователе {UserId} ({Username}) успешно получена",
            userId,
            user.Username
        );

        var personalization = await _personalizationStorage.Get(userId);

        var grpcUser = user.ToGrpc();
        grpcUser.ProfilePosterFileId = personalization?.ProfilePosterFileId ?? string.Empty;

        return new GetUserResponse { User = grpcUser };
    }
}