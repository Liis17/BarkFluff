using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.CheckExistEmail;

public class CheckExistEmailQueryHandler : IRequestHandler<CheckExistEmailQuery, CheckExistResponse>
{

    private readonly UsersStorage _usersStorage;

    public CheckExistEmailQueryHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<CheckExistResponse> Handle(CheckExistEmailQuery request, CancellationToken cancellationToken)
    {
        var userByEmail = await _usersStorage.GetUserByEmail(request.Email);

        if (userByEmail is null || userByEmail.IsDraft)
        {
            return new CheckExistResponse { Exist = false };
        }
        
        return new CheckExistResponse { Exist = true };
    }
}