using MediatR;

namespace BarkFluff.Users.Features.ChangeName;

public class ChangeNameCommand : IRequest
{
    public string FirstName { get; set; }

    public string LastName { get; set; }
}