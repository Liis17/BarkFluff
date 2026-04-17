using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Privacy.GetPrivacySettings;

public class GetPrivacySettingsQueryHandler : IRequestHandler<GetPrivacySettingsQuery, GetPrivacySettingsResponse>
{
    private readonly UserContext _userContext;
    private readonly PrivacyStorage _privacyStorage;
    private readonly ILogger<GetPrivacySettingsQueryHandler> _logger;

    public GetPrivacySettingsQueryHandler(
        UserContext userContext,
        PrivacyStorage privacyStorage,
        ILogger<GetPrivacySettingsQueryHandler> logger)
    {
        _userContext = userContext;
        _privacyStorage = privacyStorage;
        _logger = logger;
    }

    public async Task<GetPrivacySettingsResponse> Handle(GetPrivacySettingsQuery request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Запрос настроек приватности для пользователя {UserId}", _userContext.UserId);

        var privacy = await _privacyStorage.GetOrCreate(_userContext.UserId);

        return new GetPrivacySettingsResponse
        {
            Settings = privacy.ToGrpc(),
        };
    }
}
