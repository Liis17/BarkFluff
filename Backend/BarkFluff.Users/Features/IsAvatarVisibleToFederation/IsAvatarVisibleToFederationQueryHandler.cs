using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.IsAvatarVisibleToFederation;

/// <summary>
/// Отдавать ли аватар пользователя в федерацию (этап 3.4).
/// </summary>
/// <remarks>
/// Правило намеренно совпадает с тем, которым <c>GetFederatedProfile</c> (2.1) решает,
/// показывать ли <c>avatar_file_id</c>: профиль виден на сайте И
/// <c>AvatarVisibility == All</c>. Инвариант: если профиль наружу аватар отдал — файл
/// отдастся; если скрыл — <c>FetchFile</c> откажет даже по утёкшей ссылке.
///
/// <c>Friends</c> трактуется как <c>None</c>, пока нет сервиса отношений — так же, как в
/// остальных privacy-проверках проекта.
/// </remarks>
public class IsAvatarVisibleToFederationQueryHandler
    : IRequestHandler<IsAvatarVisibleToFederationQuery, IsAvatarVisibleToFederationResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly PrivacyStorage _privacyStorage;

    public IsAvatarVisibleToFederationQueryHandler(UsersStorage usersStorage, PrivacyStorage privacyStorage)
    {
        _usersStorage = usersStorage;
        _privacyStorage = privacyStorage;
    }

    public async Task<IsAvatarVisibleToFederationResponse> Handle(
        IsAvatarVisibleToFederationQuery request,
        CancellationToken cancellationToken)
    {
        var user = await _usersStorage.GetById(request.UserId);

        if (user is null || user.IsDraft)
        {
            return new IsAvatarVisibleToFederationResponse { Visible = false };
        }

        var privacy = await _privacyStorage.GetOrCreate(user.Id);

        var visible = privacy.ProfileVisibleOnSite
            && privacy.AvatarVisibility == Domain.ProfileFieldVisibility.All;

        return new IsAvatarVisibleToFederationResponse { Visible = visible };
    }
}
