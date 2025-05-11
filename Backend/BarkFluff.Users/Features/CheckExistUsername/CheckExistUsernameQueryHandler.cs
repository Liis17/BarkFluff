using BarkFluff.Proto.Users;
using BarkFluff.Users.Features.CheckExistEmail;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.CheckExistUsername;

public class CheckExistUsernameQueryHandler : IRequestHandler<CheckExistUsernameQuery, CheckExistResponse>
{
    private readonly UsersStorage _usersStorage;

    public CheckExistUsernameQueryHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }


    public async Task<CheckExistResponse> Handle(CheckExistUsernameQuery request, CancellationToken cancellationToken)
    {
        var userByUsername = await _usersStorage.GetUserByUsername(request.Username);
        
        if (userByUsername is null || userByUsername.IsDraft)
        {
            return new CheckExistResponse { Exist = false };
        }

        return new CheckExistResponse() { Exist = true };
    }
}