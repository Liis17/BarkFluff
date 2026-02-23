using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Proto.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Features.SearchUsersServer;

public class SearchUsersServerQueryHandler : IRequestHandler<SearchUsersServerQuery, SearchUsersServerResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<SearchUsersServerQueryHandler> _logger;

    public SearchUsersServerQueryHandler(UsersStorage usersStorage, ILogger<SearchUsersServerQueryHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<SearchUsersServerResponse> Handle(SearchUsersServerQuery request, CancellationToken cancellationToken)
    {
        if (request.Size <= 0)
            return new SearchUsersServerResponse();

        if (request.Size > 50)
            request.Size = 50;

        // Пустой запрос — все юзеры
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            var allResult = await _usersStorage.GetAllUsersDescending(request.Offset, request.Size);

            return new SearchUsersServerResponse
            {
                Users = { allResult.Users.Select(x => x.ToGrpc()) },
                TotalCount = allResult.TotalCount
            };
        }

        // Числовой запрос — поиск по user_id
        if (long.TryParse(request.Query.Trim(), out var userId))
        {
            var user = await _usersStorage.GetById(userId);

            if (user is null || user.IsDraft)
                return new SearchUsersServerResponse { TotalCount = 0 };

            return new SearchUsersServerResponse
            {
                Users = { user.ToGrpc() },
                TotalCount = 1
            };
        }

        // Иначе — поиск по триграммам
        var usersResult = await _usersStorage.SearchUsersByTrigram(request.Query, request.Offset, request.Size);

        return new SearchUsersServerResponse
        {
            Users = { usersResult.Users.Select(x => x.ToGrpc()) },
            TotalCount = Math.Max(usersResult.TotalCount, 0)
        };
    }
}
