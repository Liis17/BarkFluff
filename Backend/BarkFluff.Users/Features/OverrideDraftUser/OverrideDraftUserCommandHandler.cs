using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.OverrideDraftUser;

public class OverrideDraftUserCommandHandler : IRequestHandler<OverrideDraftUserCommand, AddDraftUserResponse>
{
    private readonly UsersStorage _usersStorage;

    public OverrideDraftUserCommandHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<AddDraftUserResponse> Handle(OverrideDraftUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _usersStorage.GetUserByEmail(request.Email) 
                   ?? await _usersStorage.GetUserByUsername(request.Username);

        if (user == null)
        {
            throw new UserNotFoundException();
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Contact.Email = request.Email;
        user.Username = request.Username;
        user.PasswordHash = string.Empty;
        user.ProfilePicture = null;
        user.RegistrationDate = DateTime.UtcNow;
        user.IsDraft = true;

        await _usersStorage.UpdateTrackedUser(user);
        
        return new AddDraftUserResponse { UserId = user.Id };
    }
}