using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.DeleteBotUser;

public class DeleteBotUserCommand : IRequest<DeleteBotUserResponse>
{
    public long UserId { get; set; }
}
