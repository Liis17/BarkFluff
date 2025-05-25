using MediatR;

namespace BarkFluff.Users.Features.ChangeUsername;

public class ChangeUsernameCommand : IRequest
{
    public string Username { get; set; }
}