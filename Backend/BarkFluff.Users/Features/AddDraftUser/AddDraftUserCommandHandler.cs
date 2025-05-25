using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.AddDraftUser;

public class AddDraftUserCommandHandler : IRequestHandler<AddDraftUserCommand, AddDraftUserResponse>
{
    
    private readonly UsersStorage _usersStorage;

    public AddDraftUserCommandHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<AddDraftUserResponse> Handle(AddDraftUserCommand request, CancellationToken cancellationToken)
    {
        var userByEmail = await _usersStorage.GetUserByEmail(request.Email);

        if (userByEmail != null)
        {
            if (userByEmail.IsDraft)
            {
                throw new UserIsDraftException();
            }
            
            throw new EmailExistException();
        }
        
        var userByUsername = await _usersStorage.GetUserByUsername(request.Username);

        if (userByUsername != null)
        {
            if (userByUsername.IsDraft)
            {
                throw new UserIsDraftException();
            }
            
            throw new UsernameExistException();
        }

        var user = await _usersStorage.CreateUser(request.Username, request.FirstName, request.LastName, request.Email);
        
        return new AddDraftUserResponse { UserId = user.Id };
    }
}