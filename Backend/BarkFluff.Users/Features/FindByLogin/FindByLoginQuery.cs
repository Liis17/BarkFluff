using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.FindByLogin;

public class FindByLoginQuery : IRequest<FindByLoginResponse>
{
    public string? Username { get; set; }
    
    public string? Email { get; set; }
}