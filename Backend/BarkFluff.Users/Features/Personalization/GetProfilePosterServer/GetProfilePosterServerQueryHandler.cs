using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.GetProfilePosterServer;

public class GetProfilePosterServerQueryHandler : IRequestHandler<GetProfilePosterServerQuery, GetProfilePosterServerResponse>
{
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly UsersStorage _usersStorage;
    private readonly FilesServerApi.FilesServerApiClient _filesClient;
    private readonly ILogger<GetProfilePosterServerQueryHandler> _logger;

    public GetProfilePosterServerQueryHandler(
        PersonalizationStorage personalizationStorage,
        UsersStorage usersStorage,
        FilesServerApi.FilesServerApiClient filesClient,
        ILogger<GetProfilePosterServerQueryHandler> logger)
    {
        _personalizationStorage = personalizationStorage;
        _usersStorage = usersStorage;
        _filesClient = filesClient;
        _logger = logger;
    }

    public async Task<GetProfilePosterServerResponse> Handle(GetProfilePosterServerQuery request, CancellationToken cancellationToken)
    {
        var user = await _usersStorage.GetById(request.UserId);
        if (user is null)
            throw new UserNotFoundException();
        if (user.AccountStatus != Domain.AccountStatus.Active)
            return new GetProfilePosterServerResponse { PosterUrl = string.Empty };

        var personalization = await _personalizationStorage.Get(request.UserId);

        if (personalization is null || string.IsNullOrEmpty(personalization.ProfilePosterFileId))
            return new GetProfilePosterServerResponse { PosterUrl = string.Empty };

        try
        {
            var fileData = await _filesClient.GetFileDataAsync(
                new GetFileDataRequest { FileId = personalization.ProfilePosterFileId });

            return new GetProfilePosterServerResponse
            {
                PosterUrl = fileData.FileInfo?.FileUrl ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить URL постера для пользователя {UserId}", request.UserId);
            return new GetProfilePosterServerResponse { PosterUrl = string.Empty };
        }
    }
}
