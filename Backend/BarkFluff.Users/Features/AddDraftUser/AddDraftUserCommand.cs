using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.AddDraftUser;

public class AddDraftUserCommand : IRequest<AddDraftUserResponse>
{
    public string FirstName { get; set; }

    public string LastName { get; set; }

    public string Username { get; set; }

    public string Email { get; set; }
}