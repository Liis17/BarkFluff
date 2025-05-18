using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.ListByIds;

public class ListByIdsCommandHandler : IRequestHandler<ListByIdsCommand, ListByIdsResponse>
{
    private readonly UsersStorage _usersStorage;

    public ListByIdsCommandHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<ListByIdsResponse> Handle(ListByIdsCommand request, CancellationToken cancellationToken)
    {
        var users = await _usersStorage.GetByIds(request.Ids);

        return new ListByIdsResponse { Users = { users.Select(x => x.ToGrpc()) } };
    }
}