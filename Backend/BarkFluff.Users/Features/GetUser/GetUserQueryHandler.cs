using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using Google.Protobuf.WellKnownTypes;
using MediatR;
using User = BarkFluff.Proto.Users.User;

namespace BarkFluff.Users.Features.GetUser;

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, GetUserResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly UserContext _userContext;

    public GetUserQueryHandler(UsersStorage usersStorage, UserContext userContext)
    {
        _usersStorage = usersStorage;
        _userContext = userContext;
    }

    public async Task<GetUserResponse> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        var userId = request.UserId ?? _userContext.UserId;
        var user = await _usersStorage.GetById(userId);

        if (user == null)
        {
            throw new UserNotFoundException();
        }

        return new GetUserResponse
        {
            User = user.ToGrpc()
        };
    }
} 