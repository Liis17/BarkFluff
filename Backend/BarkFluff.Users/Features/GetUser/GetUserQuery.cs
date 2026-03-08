using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.GetUser;

public class GetUserQuery : IRequest<GetUserResponse>
{
    public long? UserId { get; init; }
}