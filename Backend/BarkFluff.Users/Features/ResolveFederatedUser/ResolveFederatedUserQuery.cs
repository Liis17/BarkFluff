using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.ResolveFederatedUser;

public class ResolveFederatedUserQuery : IRequest<ResolveFederatedUserResponse>
{
    public string Fid { get; set; } = string.Empty;
}
