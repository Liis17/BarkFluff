using BarkFluff.Proto.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.CreateAccount;

public class CreateAccountCommand : IRequest<CreateAccountResponse>
{
    public string FirstName { get; set; }
    
    public string LastName { get; set; }
    
    public string Username { get; set; }
    
    public string Email { get; set; }
}