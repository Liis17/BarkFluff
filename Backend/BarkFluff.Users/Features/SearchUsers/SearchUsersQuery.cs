namespace BarkFluff.Users.Features.SearchUsers;

using MediatR;

using Proto.Users;

public class SearchUsersQuery : IRequest<SearchUsersResponse>
{
    public string Query { get; set; }

    public int Skip { get; set; }

    public int Size { get; set; }
}