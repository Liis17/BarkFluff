namespace BarkFluff.Users.Features.SearchUsers;

using Mapping;
using MediatR;
using Persistence.Services;
using Proto.Users;

public class SearchUsersQueryHandler : IRequestHandler<SearchUsersQuery, SearchUsersResponse>
{
    private readonly UsersStorage _usersStorage;

    public SearchUsersQueryHandler(UsersStorage usersStorage)
    {
        this._usersStorage = usersStorage;
    }

    public async Task<SearchUsersResponse> Handle(SearchUsersQuery request, CancellationToken cancellationToken)
    {
        if (request.Size == 0)
        {
            return new SearchUsersResponse() { Users = { } };
        }

        if (request.Size > 50)
        {
            request.Size = 50;
        }
        
        var usersResult =  await _usersStorage.SearchUsersByTrigram(request.Query, request.Skip, request.Size);
        
        return new SearchUsersResponse
        {
            Users = { usersResult.Users.Select(x => x.ToGrpc()) }, TotalCount = usersResult.TotalCount
        };
    }
}