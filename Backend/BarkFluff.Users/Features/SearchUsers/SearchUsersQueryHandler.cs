namespace BarkFluff.Users.Features.SearchUsers;

using BarkFluff.GrpcServer.XAuth;

using Mapping;

using MediatR;

using Microsoft.Extensions.Logging;

using Persistence.Services;

using Proto.Users;

public class SearchUsersQueryHandler : IRequestHandler<SearchUsersQuery, SearchUsersResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly UserContext _userContext;
    private readonly ILogger<SearchUsersQueryHandler> _logger;

    public SearchUsersQueryHandler(UsersStorage usersStorage, UserContext userContext, ILogger<SearchUsersQueryHandler> logger)
    {
        this._usersStorage = usersStorage;
        this._userContext = userContext;
        this._logger = logger;
    }

    public async Task<SearchUsersResponse> Handle(SearchUsersQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Поиск пользователей по запросу: '{Query}'. Skip: {Skip}, Size: {Size}",
            request.Query,
            request.Skip,
            request.Size
        );

        if (request.Size == 0)
        {
            _logger.LogDebug("Размер выборки равен 0, возвращаем пустой результат");
            return new SearchUsersResponse() { Users = { } };
        }

        if (request.Size > 50)
        {
            _logger.LogDebug("Ограничение размера выборки с {OriginalSize} до {NewSize}", request.Size, 50);
            request.Size = 50;
        }

        // Исключаем пользователей со скрытым SearchVisible, но всегда показываем текущего пользователя самому себе.
        var usersResult = await _usersStorage.SearchUsersByTrigram(
            request.Query,
            request.Skip,
            request.Size,
            currentUserId: _userContext.UserId,
            respectSearchVisibility: true);

        if (usersResult.TotalCount < 0)
        {
            usersResult.TotalCount = 0;
        }

        _logger.LogInformation(
            "Поиск завершен. Найдено {UserCount} пользователей из {TotalCount} по запросу '{Query}'",
            usersResult.Users.Count(),
            usersResult.TotalCount,
            request.Query
        );

        return new SearchUsersResponse
        {
            Users = { usersResult.Users.Select(x => x.ToGrpc()) },
            TotalCount = usersResult.TotalCount
        };
    }
}