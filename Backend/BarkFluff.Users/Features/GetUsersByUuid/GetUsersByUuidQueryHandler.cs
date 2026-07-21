using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.GetUsersByUuid;

// Батч-чтение профилей по UUID для Messages (этап 2.1). Локальные и remote вперемешку.
// Локальные отдаются как есть (межсервисный доверенный потребитель — Messages рендерит remote-авторов);
// draft-пользователи → found=false. Remote deactivated → found=true с is_deactivated=true.
public class GetUsersByUuidQueryHandler : IRequestHandler<GetUsersByUuidQuery, GetUsersByUuidResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly RemoteUsersStorage _remoteUsersStorage;
    private readonly PrivacyStorage _privacyStorage;
    private readonly MetricsCollector _metrics;

    public GetUsersByUuidQueryHandler(
        UsersStorage usersStorage,
        RemoteUsersStorage remoteUsersStorage,
        PrivacyStorage privacyStorage,
        MetricsCollector metrics)
    {
        _usersStorage = usersStorage;
        _remoteUsersStorage = remoteUsersStorage;
        _privacyStorage = privacyStorage;
        _metrics = metrics;
    }

    public async Task<GetUsersByUuidResponse> Handle(GetUsersByUuidQuery request, CancellationToken cancellationToken)
    {
        _metrics.Increment("users_by_uuid_lookups");

        var parsed = new List<Guid>(request.Request.Uuids.Count);
        foreach (var raw in request.Request.Uuids)
        {
            if (Guid.TryParse(raw, out var uuid))
                parsed.Add(uuid);
        }

        if (parsed.Count == 0)
            return new GetUsersByUuidResponse();

        var localUsers = await _usersStorage.GetByUuids(parsed);
        var localByUuid = localUsers.ToDictionary(u => u.Uuid);

        // Privacy для локальных (этап 2.5) — батч, без похода в БД на каждого пользователя.
        var localUserIds = localUsers.Where(u => !u.IsDraft).Select(u => u.Id).ToList();
        var privacyByUserId = localUserIds.Count > 0
            ? (await _privacyStorage.GetByUserIds(localUserIds)).ToDictionary(p => p.UserId)
            : new Dictionary<long, Domain.Privacy>();

        var remoteUuids = parsed.Where(u => !localByUuid.ContainsKey(u)).ToList();
        var remoteUsers = remoteUuids.Count > 0
            ? await _remoteUsersStorage.GetByUuidsAsync(remoteUuids)
            : new List<Domain.RemoteUser>();
        var remoteByUuid = remoteUsers.ToDictionary(r => r.Uuid);

        var response = new GetUsersByUuidResponse();
        foreach (var uuid in parsed)
        {
            if (localByUuid.TryGetValue(uuid, out var local))
            {
                if (local.IsDraft)
                {
                    response.Users.Add(new UserProfileByUuid { Uuid = uuid.ToString(), Found = false });
                    continue;
                }

                response.Users.Add(new UserProfileByUuid
                {
                    Uuid = uuid.ToString(),
                    Found = true,
                    IsRemote = false,
                    Username = local.Username,
                    FirstName = local.FirstName,
                    LastName = local.LastName,
                    Bio = local.Bio ?? string.Empty,
                    AvatarFileId = local.ProfilePicture ?? string.Empty,
                    UserId = local.Id,
                    DenyFederatedDm = privacyByUserId.TryGetValue(local.Id, out var privacy) && privacy.DenyFederatedDm,
                });
            }
            else if (remoteByUuid.TryGetValue(uuid, out var remote))
            {
                response.Users.Add(new UserProfileByUuid
                {
                    Uuid = uuid.ToString(),
                    Found = true,
                    IsRemote = true,
                    ServerName = remote.ServerName,
                    Username = remote.Username,
                    FirstName = remote.FirstName ?? string.Empty,
                    LastName = remote.LastName ?? string.Empty,
                    Bio = remote.Bio ?? string.Empty,
                    AvatarFileId = remote.AvatarFileId ?? string.Empty,
                    IsDeactivated = remote.IsDeactivated,
                });
            }
            else
            {
                response.Users.Add(new UserProfileByUuid { Uuid = uuid.ToString(), Found = false });
            }
        }

        return response;
    }
}
