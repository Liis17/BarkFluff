using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.ConfirmUser;

public class ConfirmUserCommandHandler : IRequestHandler<ConfirmUserCommand>
{
    
    private readonly UsersStorage _usersStorage;

    public ConfirmUserCommandHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task Handle(ConfirmUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _usersStorage.GetById(request.UserId);

        if (user is null)
        {
            throw new UserNotFoundException();
        }
        
        await _usersStorage.ChangeDraftStatus(request.UserId, false);
    }
}