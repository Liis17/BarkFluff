using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

namespace BarkFluff.Users.Features.UserSettings.GetUserSettings;

public class GetUserSettingsQueryHandler(
    UserContext userContext,
    UserSettingsStorage settingsStorage,
    UsersStorage usersStorage)
    : IRequestHandler<GetUserSettingsQuery, GetUserSettingsResponse>
{
    public async Task<GetUserSettingsResponse> Handle(GetUserSettingsQuery request, CancellationToken cancellationToken)
    {
        var accountStatus = await usersStorage.GetAccountStatus(userContext.UserId, cancellationToken);
        if (accountStatus is null or Domain.AccountStatus.Deleted)
            throw new UserNotFoundException();

        var settings = await settingsStorage.GetOrCreate(userContext.UserId);
        var chatSettings = await settingsStorage.GetChatSettings(userContext.UserId);
        return new GetUserSettingsResponse { Settings = settings.ToGrpc(chatSettings) };
    }
}
