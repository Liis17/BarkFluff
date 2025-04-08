using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.CreateAccount;

public class CreateAccountCommandHandler(BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient usersClient) 
    : IRequestHandler<CreateAccountCommand, CreateAccountResponse>
{
    public async Task<CreateAccountResponse> Handle(CreateAccountCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Username))
        {
            throw new UsernameOrEmailIsEmptyException();
        }

        
        return null;
    }
}