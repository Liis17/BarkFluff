using BarkFluff.Onliner.Persistence.Contexts;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;

using Google.Protobuf.WellKnownTypes;

using MediatR;

using Microsoft.EntityFrameworkCore;

using ProtoStatusTypeId = BarkFluff.Proto.Onliner.StatusTypeId;

namespace BarkFluff.Onliner.Features.GetLocalPresence;

/// <summary>
/// Статусы НАШИХ пользователей для отдачи ноде-партнёру через Federation (этап 4.2).
/// </summary>
/// <remarks>
/// Ключевое: privacy применяется здесь — у владельца данных (инвариант №27). Скрытому
/// пользователю отдаётся <c>UNKNOWN</c>, а не настоящий статус, поэтому Federation не заводит
/// копию privacy-логики и не может её случайно обойти. Проверку «а имеет ли нода-подписчик
/// право следить за этим пользователем» делает Messages (<c>CheckFederatedPresenceAccess</c>,
/// этап 4.1) — это другая проверка, она про отношения, а не про настройку приватности.
/// </remarks>
public class GetLocalPresenceQueryHandler : IRequestHandler<GetLocalPresenceQuery, GetLocalPresenceResponse>
{
    private readonly IPresenceStore _presence;
    private readonly OnlineStatusContext _dbContext;
    private readonly OnlineVisibilityFilter _visibilityFilter;

    public GetLocalPresenceQueryHandler(
        IPresenceStore presence,
        OnlineStatusContext dbContext,
        OnlineVisibilityFilter visibilityFilter)
    {
        _presence = presence;
        _dbContext = dbContext;
        _visibilityFilter = visibilityFilter;
    }

    public async Task<GetLocalPresenceResponse> Handle(
        GetLocalPresenceQuery request,
        CancellationToken cancellationToken)
    {
        var response = new GetLocalPresenceResponse();

        foreach (var userId in request.UserIds.Distinct())
        {
            // Сбой Users → fail-closed (скрыт), как и в клиентском пути.
            if (!await _visibilityFilter.IsVisibleToCaller(userId, cancellationToken))
            {
                response.Statuses.Add(BuildUnknown(userId));
                continue;
            }

            response.Statuses.Add(await GetUserStatusAsync(userId, cancellationToken));
        }

        return response;
    }

    private async Task<UserOnlineStatus> GetUserStatusAsync(long userId, CancellationToken cancellationToken)
    {
        var onlineStatus = await _presence.GetOnlineAsync(userId, cancellationToken);

        if (onlineStatus != null)
        {
            return MapToProto(onlineStatus);
        }

        var dbStatus = await _dbContext.UsersOnlineStatuses
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        return dbStatus != null ? MapToProto(dbStatus) : BuildUnknown(userId);
    }

    private static UserOnlineStatus BuildUnknown(long userId) => new()
    {
        UserId = userId,
        Status = ProtoStatusTypeId.Unknown,
        LastSeen = Timestamp.FromDateTime(DateTime.MinValue.ToUniversalTime())
    };

    private static UserOnlineStatus MapToProto(Domain.Entities.UserOnlineStatus domainStatus) => new()
    {
        UserId = domainStatus.UserId,
        Status = domainStatus.Status switch
        {
            Domain.Enums.StatusTypeId.Online => ProtoStatusTypeId.StatusOnline,
            Domain.Enums.StatusTypeId.Offline => ProtoStatusTypeId.StatusOffline,
            _ => ProtoStatusTypeId.Unknown
        },
        LastSeen = Timestamp.FromDateTime(domainStatus.LastSeen.ToUniversalTime())
    };
}
