using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

namespace BarkFluff.Users.Features.Privacy.GetUserPrivacyServer;

public class GetUserPrivacyServerQueryHandler : IRequestHandler<GetUserPrivacyServerQuery, GetUserPrivacyResponse>
{
    private readonly PrivacyStorage _privacyStorage;
    private readonly UsersStorage _usersStorage;

    public GetUserPrivacyServerQueryHandler(PrivacyStorage privacyStorage, UsersStorage usersStorage)
    {
        _privacyStorage = privacyStorage;
        _usersStorage = usersStorage;
    }

    public async Task<GetUserPrivacyResponse> Handle(GetUserPrivacyServerQuery request, CancellationToken cancellationToken)
    {
        var user = await _usersStorage.GetById(request.UserId);
        if (user is null)
            throw new UserNotFoundException();

        var privacy = await _privacyStorage.GetOrCreate(request.UserId);
        if (user.AccountStatus != Domain.AccountStatus.Active)
            privacy.OnlineVisibility = Domain.ProfileFieldVisibility.None;

        return new GetUserPrivacyResponse
        {
            Settings = privacy.ToGrpc(),
        };
    }
}
