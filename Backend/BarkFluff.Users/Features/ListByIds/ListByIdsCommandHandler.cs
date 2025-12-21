using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Users.Features.ListByIds;

public class ListByIdsCommandHandler : IRequestHandler<ListByIdsCommand, ListByIdsResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<ListByIdsCommandHandler> _logger;

    public ListByIdsCommandHandler(UsersStorage usersStorage, ILogger<ListByIdsCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<ListByIdsResponse> Handle(ListByIdsCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Получение списка пользователей по ID. Количество запрошенных ID: {Count}",
            request.Ids.Count()
        );

        var users = await _usersStorage.GetByIds(request.Ids);

        _logger.LogInformation(
            "Получено {UserCount} пользователей из {RequestedCount} запрошенных ID",
            users.Count(),
            request.Ids.Count()
        );

        return new ListByIdsResponse { Users = { users.Select(x => x.ToGrpc()) } };
    }
}