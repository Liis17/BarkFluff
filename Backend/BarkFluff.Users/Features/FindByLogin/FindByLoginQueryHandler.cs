using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;

namespace BarkFluff.Users.Features.FindByLogin;

public class FindByLoginQueryHandler : IRequestHandler<FindByLoginQuery, FindByLoginResponse>
{
    
    private readonly UsersStorage _usersStorage;

    public FindByLoginQueryHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<FindByLoginResponse> Handle(FindByLoginQuery request, CancellationToken cancellationToken)
    {

        Domain.User? user = null;
        if (!string.IsNullOrEmpty(request.Username))
        {
             user = await _usersStorage.GetUserByUsername(request.Username);
        }

        if (!string.IsNullOrEmpty(request.Email))
        {
            user = await _usersStorage.GetUserByEmail(request.Email);
        }

        if (user is null)
        {
            throw new UserNotFoundException();
        }
        
        return new FindByLoginResponse { User = user.ToGrpc() };
    }
}