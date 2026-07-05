using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

using FilesServerApiClient = BarkFluff.Proto.Files.FilesServerApi.FilesServerApiClient;

namespace BarkFluff.Users.Features.GetUserByUsername;

public class GetUserByUsernameQueryHandler : IRequestHandler<GetUserByUsernameQuery, GetUserByUsernameResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly PrivacyStorage _privacyStorage;
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly FilesServerApiClient _filesClient;
    private readonly MetricsCollector _metrics;

    public GetUserByUsernameQueryHandler(
        UsersStorage usersStorage,
        PrivacyStorage privacyStorage,
        PersonalizationStorage personalizationStorage,
        FilesServerApiClient filesClient,
        MetricsCollector metrics)
    {
        _usersStorage = usersStorage;
        _privacyStorage = privacyStorage;
        _personalizationStorage = personalizationStorage;
        _filesClient = filesClient;
        _metrics = metrics;
    }

    public async Task<GetUserByUsernameResponse> Handle(GetUserByUsernameQuery request, CancellationToken cancellationToken)
    {
        var user = await _usersStorage.GetUserByUsername(request.Username?.Trim());

        if (user is null || user.IsDraft)
        {
            _metrics.Increment("public_profile_not_found");
            return new GetUserByUsernameResponse { Found = false };
        }

        // Применение настроек приватности к публичной странице профиля.
        // FRIENDS пока трактуется как NONE — в бэкенде нет системы отношений между пользователями.
        var privacy = await _privacyStorage.GetOrCreate(user.Id);

        if (!privacy.ProfileVisibleOnSite)
        {
            _metrics.Increment("public_profile_hidden");
            return new GetUserByUsernameResponse { Found = false };
        }

        var bio = privacy.BioVisibility == Domain.ProfileFieldVisibility.All
            ? (user.Bio ?? string.Empty)
            : string.Empty;

        var avatar = privacy.AvatarVisibility == Domain.ProfileFieldVisibility.All
            ? (user.ProfilePicture ?? string.Empty)
            : string.Empty;

        // Получаем постер профиля через персонализацию
        var posterUrl = string.Empty;
        try
        {
            var personalization = await _personalizationStorage.Get(user.Id);
            if (!string.IsNullOrEmpty(personalization?.ProfilePosterFileId))
            {
                var fileDataResponse = await _filesClient.GetFileDataAsync(
                    new GetFileDataRequest { FileId = personalization.ProfilePosterFileId });
                posterUrl = fileDataResponse.FileInfo.FileUrl ?? string.Empty;
                _metrics.Increment("files_fetch_success");
            }
        }
        catch (Exception)
        {
            _metrics.Increment("files_fetch_errors");
            // Не блокируем ответ, если постер недоступен
            posterUrl = string.Empty;
        }

        return new GetUserByUsernameResponse
        {
            Found = true,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Username = user.Username,
            Bio = bio,
            ProfilePicture = avatar,
            ProfilePosterUrl = posterUrl,
            IsBot = user.IsBot,
        };
    }
}
