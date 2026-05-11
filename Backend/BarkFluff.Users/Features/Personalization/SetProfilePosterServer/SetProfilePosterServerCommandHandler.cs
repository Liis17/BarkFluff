using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.SetProfilePosterServer;

public class SetProfilePosterServerCommandHandler : IRequestHandler<SetProfilePosterServerCommand, SetProfilePosterServerResponse>
{
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly ILogger<SetProfilePosterServerCommandHandler> _logger;

    public SetProfilePosterServerCommandHandler(
        PersonalizationStorage personalizationStorage,
        ILogger<SetProfilePosterServerCommandHandler> logger)
    {
        _personalizationStorage = personalizationStorage;
        _logger = logger;
    }

    public async Task<SetProfilePosterServerResponse> Handle(SetProfilePosterServerCommand request, CancellationToken cancellationToken)
    {
        await _personalizationStorage.UpdatePoster(request.UserId, request.PosterFileId);

        _logger.LogInformation(
            "Постер профиля обновлён для пользователя {UserId} (серверный): {FileId}",
            request.UserId,
            request.PosterFileId ?? "(удалён)");

        return new SetProfilePosterServerResponse();
    }
}
