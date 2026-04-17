using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Privacy.UpdatePrivacySettings;

public class UpdatePrivacySettingsCommandHandler : IRequestHandler<UpdatePrivacySettingsCommand>
{
    private readonly UserContext _userContext;
    private readonly PrivacyStorage _privacyStorage;
    private readonly ILogger<UpdatePrivacySettingsCommandHandler> _logger;

    public UpdatePrivacySettingsCommandHandler(
        UserContext userContext,
        PrivacyStorage privacyStorage,
        ILogger<UpdatePrivacySettingsCommandHandler> logger)
    {
        _userContext = userContext;
        _privacyStorage = privacyStorage;
        _logger = logger;
    }

    public async Task Handle(UpdatePrivacySettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = request.Settings ?? new Proto.Users.PrivacySettings();
        var updated = settings.ToDomain();

        await _privacyStorage.Update(_userContext.UserId, updated);

        _logger.LogInformation(
            "Настройки приватности обновлены для пользователя {UserId}: " +
            "ProfileVisibleOnSite={ProfileVisibleOnSite}, Avatar={Avatar}, Bio={Bio}, Email={Email}, Search={SearchVisible}, Online={Online}",
            _userContext.UserId,
            updated.ProfileVisibleOnSite, updated.AvatarVisibility, updated.BioVisibility,
            updated.EmailVisibility, updated.SearchVisible, updated.OnlineVisibility);
    }
}
