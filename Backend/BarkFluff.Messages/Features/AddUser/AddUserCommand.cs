using MediatR;

namespace BarkFluff.Messages.Features.AddUser;

public class AddUserCommand : IRequest
{
    public Guid ChatId { get; set; }

    public long UserId { get; set; }
}
