using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.CreateBotUser;

public class CreateBotUserCommand : IRequest<CreateBotUserResponse>
{
    public string Username { get; set; }

    public string FirstName { get; set; }

    public bool BypassUsernameRules { get; set; }
}
