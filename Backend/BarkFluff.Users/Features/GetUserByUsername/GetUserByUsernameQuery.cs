using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.GetUserByUsername;

public class GetUserByUsernameQuery : IRequest<GetUserByUsernameResponse>
{
    public string? Username { get; set; }
}
