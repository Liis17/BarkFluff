using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.GetProfilePoster;

public class GetProfilePosterQueryHandler : IRequestHandler<GetProfilePosterQuery, GetProfilePosterResponse>
{
    private readonly UserContext _userContext;
    private readonly PersonalizationStorage _personalizationStorage;

    public GetProfilePosterQueryHandler(UserContext userContext, PersonalizationStorage personalizationStorage)
    {
        _userContext = userContext;
        _personalizationStorage = personalizationStorage;
    }

    public async Task<GetProfilePosterResponse> Handle(GetProfilePosterQuery request, CancellationToken cancellationToken)
    {
        var personalization = await _personalizationStorage.Get(_userContext.UserId);

        return new GetProfilePosterResponse
        {
            ProfilePosterFileId = personalization?.ProfilePosterFileId ?? string.Empty,
        };
    }
}
