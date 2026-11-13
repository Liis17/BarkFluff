using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

namespace BarkFluff.Users.Features.UserSettings.SetGlobalChatBackground;

public class SetGlobalChatBackgroundCommandHandler(
    UserContext userContext,
    UserSettingsStorage settingsStorage,
    PersonalizationStorage personalizationStorage,
    UsersStorage usersStorage)
    : IRequestHandler<SetGlobalChatBackgroundCommand>
{
    public async Task Handle(SetGlobalChatBackgroundCommand request, CancellationToken cancellationToken)
    {
        var accountStatus = await usersStorage.GetAccountStatus(userContext.UserId, cancellationToken);
        if (accountStatus is null or Domain.AccountStatus.Deleted)
            throw new UserNotFoundException();

        if (!string.IsNullOrEmpty(request.FileId)
            && !await personalizationStorage.HasChatBackgroundFile(userContext.UserId, request.FileId))
        {
            throw new InvalidOperationException("Фон не найден в коллекции пользователя");
        }

        await settingsStorage.SetGlobalChatBackground(userContext.UserId, request.FileId);
    }
}
