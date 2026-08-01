using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.UserSettings.SetChatBackground;

public class SetChatBackgroundCommandHandler(
    UserContext userContext,
    UserSettingsStorage settingsStorage,
    PersonalizationStorage personalizationStorage)
    : IRequestHandler<SetChatBackgroundCommand>
{
    public async Task Handle(SetChatBackgroundCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.FileId)
            && !await personalizationStorage.HasChatBackgroundFile(userContext.UserId, request.FileId))
        {
            throw new InvalidOperationException("Фон не найден в коллекции пользователя");
        }

        await settingsStorage.SetChatBackground(userContext.UserId, request.ChatId, request.FileId);
    }
}
