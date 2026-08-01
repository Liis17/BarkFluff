using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.UserSettings.GetUserSettings;

public class GetUserSettingsQueryHandler(
    UserContext userContext,
    UserSettingsStorage settingsStorage)
    : IRequestHandler<GetUserSettingsQuery, GetUserSettingsResponse>
{
    public async Task<GetUserSettingsResponse> Handle(GetUserSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await settingsStorage.GetOrCreate(userContext.UserId);
        var chatSettings = await settingsStorage.GetChatSettings(userContext.UserId);
        return new GetUserSettingsResponse { Settings = settings.ToGrpc(chatSettings) };
    }
}
