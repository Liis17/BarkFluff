using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.UpdatePersonalization;

public class UpdatePersonalizationCommandHandler : IRequestHandler<UpdatePersonalizationCommand>
{
    private readonly UserContext _userContext;
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly ILogger<UpdatePersonalizationCommandHandler> _logger;

    public UpdatePersonalizationCommandHandler(
        UserContext userContext,
        PersonalizationStorage personalizationStorage,
        ILogger<UpdatePersonalizationCommandHandler> logger)
    {
        _userContext = userContext;
        _personalizationStorage = personalizationStorage;
        _logger = logger;
    }

    public async Task Handle(UpdatePersonalizationCommand request, CancellationToken cancellationToken)
    {
        var data = request.Personalization ?? new Proto.Users.UserPersonalizationData();

        var profilePosterFileId = string.IsNullOrEmpty(data.ProfilePosterFileId)
            ? null
            : data.ProfilePosterFileId;

        var chatBackgroundFileIds = data.ChatBackgroundFileIds.ToArray();

        await _personalizationStorage.Update(_userContext.UserId, profilePosterFileId, chatBackgroundFileIds);

        _logger.LogInformation(
            "Персонализация обновлена для пользователя {UserId}: ProfilePoster={ProfilePoster}, ChatBackgrounds={Count}",
            _userContext.UserId,
            profilePosterFileId ?? "(none)",
            chatBackgroundFileIds.Length);
    }
}
