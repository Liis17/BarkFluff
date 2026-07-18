using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.FederationInternal;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;
using BarkFluff.Users.Services;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Users.Features.ResolveFederatedUser;

// Клиентский резолв @username:servername (этап 2.1).
// 1) Локальный FID → поиск по username (без servername).
// 2) Remote FID → кеш в RemoteUsers (TTL 24ч); при отсутствии/протухании — Federation.ResolveRemoteUser
//    и upsert кеша. Federation:Enabled=false (если ключ виден Users) — found=false без сетевого вызова.
public class ResolveFederatedUserQueryHandler : IRequestHandler<ResolveFederatedUserQuery, ResolveFederatedUserResponse>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly UsersStorage _usersStorage;
    private readonly RemoteUsersStorage _remoteUsersStorage;
    private readonly FederationInternalApi.FederationInternalApiClient _federationClient;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;

    public ResolveFederatedUserQueryHandler(
        UsersStorage usersStorage,
        RemoteUsersStorage remoteUsersStorage,
        FederationInternalApi.FederationInternalApiClient federationClient,
        IConfiguration configuration,
        MetricsCollector metrics)
    {
        _usersStorage = usersStorage;
        _remoteUsersStorage = remoteUsersStorage;
        _federationClient = federationClient;
        _configuration = configuration;
        _metrics = metrics;
    }

    public async Task<ResolveFederatedUserResponse> Handle(ResolveFederatedUserQuery request, CancellationToken cancellationToken)
    {
        var ownServerName = _configuration["Federation:ServerName"];

        if (!FidParser.TryParse(request.Fid, ownServerName, out var fid) || fid is null)
        {
            _metrics.Increment("federated_resolves.invalid");
            return new ResolveFederatedUserResponse();
        }

        if (fid.IsLocal)
        {
            var local = await _usersStorage.GetUserByUsername(fid.Username);
            if (local is null || local.IsDraft)
            {
                _metrics.Increment("federated_resolves.not_found");
                return new ResolveFederatedUserResponse();
            }

            _metrics.Increment("federated_resolves.local");
            return ToResponse(local.Uuid, local.Username, ownServerName ?? string.Empty,
                local.FirstName, local.LastName, local.Bio, local.ProfilePicture);
        }

        // Federation:Enabled сейчас имеет ServiceId=15 — Users этот ключ не видит через LoadConfiguration.
        // Если ключ всё-таки попал (например, через env/appsettings) и не равен "true" — гейтим локально.
        var enabled = _configuration["Federation:Enabled"];
        if (enabled is not null && !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            _metrics.Increment("federated_resolves.disabled");
            return new ResolveFederatedUserResponse();
        }

        var cached = await _remoteUsersStorage.GetAsync(fid.Username, fid.ServerName!);
        if (cached is not null && !cached.IsDeactivated && DateTime.UtcNow - cached.LastSyncedAt < CacheTtl)
        {
            _metrics.Increment("federated_resolves.cache");
            return ToResponse(cached.Uuid, cached.Username, cached.ServerName,
                cached.FirstName, cached.LastName, cached.Bio, cached.AvatarFileId);
        }

        ResolveRemoteUserResponse federationResponse;
        try
        {
            federationResponse = await _federationClient.ResolveRemoteUserAsync(
                new ResolveRemoteUserRequest { Fid = $"{fid.Username}:{fid.ServerName}" },
                cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
        {
            // Federation не реализовала RPC (например, старая версия сервиса) — не ретраим.
            _metrics.Increment("federated_resolves.unavailable");
            return new ResolveFederatedUserResponse();
        }
        catch (RpcException)
        {
            // Сетевая ошибка / пир недоступен — клиенту честный found=false, не валить запрос.
            _metrics.Increment("federated_resolves.error");
            return new ResolveFederatedUserResponse();
        }

        if (!federationResponse.Found || federationResponse.Profile is null)
        {
            _metrics.Increment("federated_resolves.not_found");
            return new ResolveFederatedUserResponse();
        }

        var profile = federationResponse.Profile;
        if (!Guid.TryParse(profile.Uuid, out var remoteUuid))
        {
            _metrics.Increment("federated_resolves.error");
            return new ResolveFederatedUserResponse();
        }

        var upsert = await _remoteUsersStorage.UpsertAsync(
            remoteUuid,
            profile.Username,
            federationResponse.ServerName,
            string.IsNullOrEmpty(profile.FirstName) ? null : profile.FirstName,
            string.IsNullOrEmpty(profile.LastName) ? null : profile.LastName,
            string.IsNullOrEmpty(profile.Bio) ? null : profile.Bio,
            ExtractAvatarFileId(profile.Avatar),
            isDeactivated: false,
            cancellationToken);

        if (upsert.Status != RemoteUsersStorage.UpsertStatus.Ok || upsert.Record is null)
        {
            _metrics.Increment("federated_resolves.upsert_rejected");
            return new ResolveFederatedUserResponse();
        }

        _metrics.Increment("federated_resolves.resolved");
        return ToResponse(upsert.Record.Uuid, upsert.Record.Username, upsert.Record.ServerName,
            upsert.Record.FirstName, upsert.Record.LastName, upsert.Record.Bio, upsert.Record.AvatarFileId);
    }

    private static string? ExtractAvatarFileId(BarkFluff.Proto.Federation.FederatedFileRef? avatar)
        => string.IsNullOrEmpty(avatar?.FileId) ? null : avatar.FileId;

    private static ResolveFederatedUserResponse ToResponse(
        Guid uuid,
        string username,
        string serverName,
        string? firstName,
        string? lastName,
        string? bio,
        string? avatarFileId)
    {
        return new ResolveFederatedUserResponse
        {
            Found = true,
            Uuid = uuid.ToString(),
            Username = username,
            ServerName = serverName,
            FirstName = firstName ?? string.Empty,
            LastName = lastName ?? string.Empty,
            Bio = bio ?? string.Empty,
            AvatarUrl = avatarFileId ?? string.Empty,
        };
    }
}
