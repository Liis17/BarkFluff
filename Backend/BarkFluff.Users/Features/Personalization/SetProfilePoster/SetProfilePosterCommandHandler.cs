using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.SetProfilePoster;

public class SetProfilePosterCommandHandler : IRequestHandler<SetProfilePosterCommand>
{
    private readonly UserContext _userContext;
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly ILogger<SetProfilePosterCommandHandler> _logger;

    public SetProfilePosterCommandHandler(
        UserContext userContext,
        PersonalizationStorage personalizationStorage,
        ILogger<SetProfilePosterCommandHandler> logger)
    {
        _userContext = userContext;
        _personalizationStorage = personalizationStorage;
        _logger = logger;
    }

    public async Task Handle(SetProfilePosterCommand request, CancellationToken cancellationToken)
    {
        await _personalizationStorage.UpdatePoster(_userContext.UserId, request.ProfilePosterFileId);

        _logger.LogInformation(
            "Постер профиля обновлён для пользователя {UserId}: {FileId}",
            _userContext.UserId,
            request.ProfilePosterFileId ?? "(удалён)");
    }
}
