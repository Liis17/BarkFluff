using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

namespace BarkFluff.Users.Features.Privacy.GetPrivacySettings;

public class GetPrivacySettingsQueryHandler : IRequestHandler<GetPrivacySettingsQuery, GetPrivacySettingsResponse>
{
    private readonly UserContext _userContext;
    private readonly PrivacyStorage _privacyStorage;
    private readonly ILogger<GetPrivacySettingsQueryHandler> _logger;
    private readonly UsersStorage _usersStorage;

    public GetPrivacySettingsQueryHandler(
        UserContext userContext,
        PrivacyStorage privacyStorage,
        UsersStorage usersStorage,
        ILogger<GetPrivacySettingsQueryHandler> logger)
    {
        _userContext = userContext;
        _privacyStorage = privacyStorage;
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<GetPrivacySettingsResponse> Handle(GetPrivacySettingsQuery request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Запрос настроек приватности для пользователя {UserId}", _userContext.UserId);

        var accountStatus = await _usersStorage.GetAccountStatus(_userContext.UserId, cancellationToken);
        if (accountStatus is null or Domain.AccountStatus.Deleted)
            throw new UserNotFoundException();

        var privacy = await _privacyStorage.GetOrCreate(_userContext.UserId);

        return new GetPrivacySettingsResponse
        {
            Settings = privacy.ToGrpc(),
        };
    }
}
