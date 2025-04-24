using MediatR;

namespace BarkFluff.Users.Features.SetPassword;

public class SetPasswordCommand : IRequest
{
    public string Password { get; set; }
}