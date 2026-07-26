using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.UpsertRemoteUsers;

// Батч-upsert кешей remote-профилей от Federation. Применяет правила пиннинга (RemoteUsersStorage)
// и возвращает per-запись результат. Невалидные записи (uuid-local-collision / server-mismatch)
// НЕ валид весь батч — остальные применяются.
public class UpsertRemoteUsersCommandHandler : IRequestHandler<UpsertRemoteUsersCommand, UpsertRemoteUsersResponse>
{
    private readonly RemoteUsersStorage _storage;
    private readonly MetricsCollector _metrics;

    public UpsertRemoteUsersCommandHandler(RemoteUsersStorage storage, MetricsCollector metrics)
    {
        _storage = storage;
        _metrics = metrics;
    }

    public async Task<UpsertRemoteUsersResponse> Handle(UpsertRemoteUsersCommand request, CancellationToken cancellationToken)
    {
        var response = new UpsertRemoteUsersResponse();

        foreach (var record in request.Request.Records)
        {
            if (!Guid.TryParse(record.Uuid, out var uuid))
            {
                response.Results.Add(new UpsertRemoteUserResult
                {
                    Uuid = record.Uuid,
                    Ok = false,
                    RejectReason = "InvalidUuid",
                });
                _metrics.Increment("remote_users_upsert_rejected.invalid_uuid");
                continue;
            }

            var result = await _storage.UpsertAsync(
                uuid,
                record.Username,
                record.ServerName,
                string.IsNullOrEmpty(record.FirstName) ? null : record.FirstName,
                string.IsNullOrEmpty(record.LastName) ? null : record.LastName,
                string.IsNullOrEmpty(record.Bio) ? null : record.Bio,
                string.IsNullOrEmpty(record.AvatarFileId) ? null : record.AvatarFileId,
                record.IsDeactivated,
                cancellationToken);

            switch (result.Status)
            {
                case RemoteUsersStorage.UpsertStatus.Ok:
                    response.Results.Add(new UpsertRemoteUserResult { Uuid = record.Uuid, Ok = true });
                    break;
                case RemoteUsersStorage.UpsertStatus.RejectedLocalUuidCollision:
                    response.Results.Add(new UpsertRemoteUserResult
                    {
                        Uuid = record.Uuid,
                        Ok = false,
                        RejectReason = "LocalUuidCollision",
                    });
                    _metrics.Increment("remote_users_upsert_rejected.local_uuid_collision");
                    break;
                case RemoteUsersStorage.UpsertStatus.RejectedServerNameMismatch:
                    response.Results.Add(new UpsertRemoteUserResult
                    {
                        Uuid = record.Uuid,
                        Ok = false,
                        RejectReason = "ServerNameMismatch",
                    });
                    _metrics.Increment("remote_users_upsert_rejected.server_name_mismatch");
                    break;
            }
        }

        return response;
    }
}
