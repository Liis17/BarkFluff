using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.GetProfilePoster;

public class GetProfilePosterQueryHandler : IRequestHandler<GetProfilePosterQuery, GetProfilePosterResponse>
{
    private readonly UserContext _userContext;
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly UsersStorage _usersStorage;

    public GetProfilePosterQueryHandler(UserContext userContext, PersonalizationStorage personalizationStorage, UsersStorage usersStorage)
    {
        _userContext = userContext;
        _personalizationStorage = personalizationStorage;
        _usersStorage = usersStorage;
    }

    public async Task<GetProfilePosterResponse> Handle(GetProfilePosterQuery request, CancellationToken cancellationToken)
    {
        var user = await _usersStorage.GetById(_userContext.UserId);
        if (user is null)
            throw new UserNotFoundException();
        if (user.AccountStatus != Domain.AccountStatus.Active)
            return new GetProfilePosterResponse();

        var personalization = await _personalizationStorage.Get(_userContext.UserId);

        return new GetProfilePosterResponse
        {
            ProfilePosterFileId = personalization?.ProfilePosterFileId ?? string.Empty,
        };
    }
}
