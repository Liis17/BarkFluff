using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.SearchUsersServer;

public class SearchUsersServerQuery : IRequest<SearchUsersServerResponse>
{
    public string Query { get; set; } = string.Empty;

    public int Offset { get; set; }

    public int Size { get; set; }
}
