using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using Google.Protobuf.WellKnownTypes;

using MediatR;

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

        List<Domain.User> users;
        int totalCount;

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            var allResult = await _usersStorage.GetAllUsersDescending(request.Offset, request.Size);
            users = allResult.Users;
            totalCount = allResult.TotalCount;
        }
        else if (long.TryParse(request.Query.Trim(), out var userId))
        {
            var user = await _usersStorage.GetById(userId);

            if (user is null || user.IsDraft)
                return new SearchUsersServerResponse { TotalCount = 0 };

            users = new List<Domain.User> { user };
            totalCount = 1;
        }
        else
        {
            // Админ-панели требуется полная видимость — настройки приватности (SearchVisible)
            // здесь сознательно игнорируются.
            var usersResult = await _usersStorage.SearchUsersByTrigram(
                request.Query, request.Offset, request.Size,
                respectSearchVisibility: false);
            users = usersResult.Users;
            totalCount = Math.Max(usersResult.TotalCount, 0);
        }

        // Конвертируем и загружаем бейджи для каждого пользователя
        var protoUsers = new List<User>();
        foreach (var user in users)
        {
            var protoUser = user.ToGrpc();

            var badges = await _usersStorage.GetUserBadgesAsync(user.Id);
            foreach (var ub in badges)
            {
                protoUser.Badges.Add(new UserBadge
                {
                    Badge = new Badge
                    {
                        Id = ub.Badge.Id,
                        Name = ub.Badge.Name,
                        Description = ub.Badge.Description ?? string.Empty,
                        ImageUrl = ub.Badge.ImageUrl ?? string.Empty,
                        IsActive = ub.Badge.IsActive,
                        CreatedDate = Timestamp.FromDateTime(DateTime.SpecifyKind(ub.Badge.CreatedDate, DateTimeKind.Utc))
                    },
                    Priority = ub.Priority,
                    AssignedDate = Timestamp.FromDateTime(DateTime.SpecifyKind(ub.AssignedDate, DateTimeKind.Utc))
                });
            }

            protoUsers.Add(protoUser);
        }

        return new SearchUsersServerResponse
        {
            Users = { protoUsers },
            TotalCount = totalCount
        };
    }
}
