using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Personalization.UpdatePersonalization;

public class UpdatePersonalizationCommandHandler : IRequestHandler<UpdatePersonalizationCommand>
{
    private readonly UserContext _userContext;
    private readonly PersonalizationStorage _personalizationStorage;
    private readonly UserSettingsStorage _userSettingsStorage;
    private readonly ILogger<UpdatePersonalizationCommandHandler> _logger;

    public UpdatePersonalizationCommandHandler(
        UserContext userContext,
        PersonalizationStorage personalizationStorage,
        UserSettingsStorage userSettingsStorage,
        ILogger<UpdatePersonalizationCommandHandler> logger)
    {
        _userContext = userContext;
        _personalizationStorage = personalizationStorage;
        _userSettingsStorage = userSettingsStorage;
        _logger = logger;
    }

    public async Task Handle(UpdatePersonalizationCommand request, CancellationToken cancellationToken)
    {
        var data = request.Personalization ?? new Proto.Users.UserPersonalizationData();

        var profilePosterFileId = string.IsNullOrEmpty(data.ProfilePosterFileId)
            ? null
            : data.ProfilePosterFileId;

        var chatBackgroundFileIds = data.ChatBackgroundFileIds.ToArray();

        var previous = await _personalizationStorage.GetOrCreate(_userContext.UserId);
        var removedFileIds = previous.ChatBackgroundFileIds.Except(chatBackgroundFileIds).ToArray();

        await _personalizationStorage.Update(_userContext.UserId, profilePosterFileId, chatBackgroundFileIds);
        await _userSettingsStorage.ClearBackgroundReferences(_userContext.UserId, removedFileIds);

        _logger.LogInformation(
            "Персонализация обновлена для пользователя {UserId}: ProfilePoster={ProfilePoster}, ChatBackgrounds={Count}",
            _userContext.UserId,
            profilePosterFileId ?? "(none)",
            chatBackgroundFileIds.Length);
    }
}
