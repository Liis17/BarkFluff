using MediatR;

namespace BarkFluff.Users.Features.ConfirmUser;

public class ConfirmUserCommand : IRequest
{
    public long UserId { get; set; }
}