using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.UserSettings.SetGlobalChatBackground;

public class SetGlobalChatBackgroundCommandHandler(
    UserContext userContext,
    UserSettingsStorage settingsStorage,
    PersonalizationStorage personalizationStorage)
    : IRequestHandler<SetGlobalChatBackgroundCommand>
{
    public async Task Handle(SetGlobalChatBackgroundCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.FileId)
            && !await personalizationStorage.HasChatBackgroundFile(userContext.UserId, request.FileId))
        {
            throw new InvalidOperationException("Фон не найден в коллекции пользователя");
        }

        await settingsStorage.SetGlobalChatBackground(userContext.UserId, request.FileId);
    }
}
