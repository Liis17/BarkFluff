using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.CheckExistUsername;

public class CheckExistUsernameQuery : IRequest<CheckExistResponse>
{
    public string Username { get; set; }
}