using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.GetFederatedProfile;

// Privacy-фильтрованный профиль локального пользователя для S2S-отдачи (Federation.GetUserProfile).
// Логика фильтрации переиспользована из GetUserByUsernameQueryHandler:
// - профиль скрыт целиком (ProfileVisibleOnSite=false) → found=false
// - draft/deactivated → found=false
// - bio/avatar по своим visibility (FRIENDS трактуется как NONE — нет системы отношений)
public class GetFederatedProfileQueryHandler : IRequestHandler<GetFederatedProfileQuery, GetFederatedProfileResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly PrivacyStorage _privacyStorage;
    private readonly MetricsCollector _metrics;

    public GetFederatedProfileQueryHandler(
        UsersStorage usersStorage,
        PrivacyStorage privacyStorage,
        MetricsCollector metrics)
    {
        _usersStorage = usersStorage;
        _privacyStorage = privacyStorage;
        _metrics = metrics;
    }

    public async Task<GetFederatedProfileResponse> Handle(GetFederatedProfileQuery request, CancellationToken cancellationToken)
    {
        Domain.User? user;
        if (request.Uuid.HasValue)
        {
            user = await _usersStorage.GetByUuid(request.Uuid.Value);
        }
        else
        {
            user = await _usersStorage.GetUserByUsername(request.Username?.Trim() ?? string.Empty);
        }

        if (user is null || user.IsDraft)
        {
            _metrics.Increment("federated_profile_not_found");
            return new GetFederatedProfileResponse { Found = false };
        }

        var privacy = await _privacyStorage.GetOrCreate(user.Id);

        if (!privacy.ProfileVisibleOnSite)
        {
            _metrics.Increment("federated_profile_hidden");
            return new GetFederatedProfileResponse { Found = false };
        }

        var bio = privacy.BioVisibility == Domain.ProfileFieldVisibility.All
            ? (user.Bio ?? string.Empty)
            : string.Empty;

        var avatar = privacy.AvatarVisibility == Domain.ProfileFieldVisibility.All
            ? (user.ProfilePicture ?? string.Empty)
            : string.Empty;

        _metrics.Increment("federated_profile_served");

        return new GetFederatedProfileResponse
        {
            Found = true,
            Uuid = user.Uuid.ToString(),
            Username = user.Username,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Bio = bio,
            AvatarFileId = avatar,
        };
    }
}
