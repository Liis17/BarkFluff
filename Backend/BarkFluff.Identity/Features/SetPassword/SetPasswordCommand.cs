namespace BarkFluff.Identity.Features.SetPassword;

using MediatR;

public class SetPasswordCommand : IRequest
{
    public string NewPassword { get; set; }
}