using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.GetProfilePosterServer;

public class GetProfilePosterServerQueryHandler : IRequestHandler<GetProfilePosterServerQuery, GetProfilePosterServerResponse>
{
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly FilesServerApi.FilesServerApiClient _filesClient;
    private readonly ILogger<GetProfilePosterServerQueryHandler> _logger;

    public GetProfilePosterServerQueryHandler(
        PersonalizationStorage personalizationStorage,
        FilesServerApi.FilesServerApiClient filesClient,
        ILogger<GetProfilePosterServerQueryHandler> logger)
    {
        _personalizationStorage = personalizationStorage;
        _filesClient = filesClient;
        _logger = logger;
    }

    public async Task<GetProfilePosterServerResponse> Handle(GetProfilePosterServerQuery request, CancellationToken cancellationToken)
    {
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
