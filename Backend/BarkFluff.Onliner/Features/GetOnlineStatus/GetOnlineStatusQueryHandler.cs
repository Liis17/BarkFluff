using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Onliner.Persistence.Contexts;
using BarkFluff.Onliner.Services;
using BarkFluff.Proto.Onliner;

using Google.Protobuf.WellKnownTypes;

using MediatR;

using Microsoft.EntityFrameworkCore;

using ProtoStatusTypeId = BarkFluff.Proto.Onliner.StatusTypeId;

namespace BarkFluff.Onliner.Features.GetOnlineStatus;

public class GetOnlineStatusQueryHandler : IRequestHandler<GetOnlineStatusQuery, GetOnlineStatusResponse>
{
    private readonly OnlineStatusStorage _storage;
    private readonly OnlineStatusContext _dbContext;
    private readonly UserContext _userContext;
    private readonly OnlineVisibilityFilter _visibilityFilter;
    private readonly ILogger<GetOnlineStatusQueryHandler> _logger;

    public GetOnlineStatusQueryHandler(
        OnlineStatusStorage storage,
        OnlineStatusContext dbContext,
        UserContext userContext,
        OnlineVisibilityFilter visibilityFilter,
        ILogger<GetOnlineStatusQueryHandler> logger)
    {
        _storage = storage;
        _dbContext = dbContext;
        _userContext = userContext;
        _visibilityFilter = visibilityFilter;
        _logger = logger;
    }

    public async Task<GetOnlineStatusResponse> Handle(
        GetOnlineStatusQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting online status for {Count} users", request.UserIds.Count);

        var callerId = _userContext.UserId;
        var visibleIds = await _visibilityFilter.GetVisibleUserIdsAsync(
            request.UserIds, callerId, cancellationToken);

        var response = new GetOnlineStatusResponse();

        foreach (var userId in request.UserIds)
        {
            if (!visibleIds.Contains(userId))
            {
                response.UsersStatuses.Add(BuildHiddenStatus(userId));
                continue;
            }

            var status = await GetUserStatusAsync(userId, cancellationToken);
            response.UsersStatuses.Add(status);
        }

        return response;
    }

    private async Task<UserOnlineStatus> GetUserStatusAsync(long userId, CancellationToken cancellationToken)
    {
        // Приоритет 1: проверяем in-memory storage
        var memoryStatus = _storage.GetStatus(userId);

        if (memoryStatus != null)
        {
            _logger.LogTrace("User {UserId} status found in memory: {Status}", userId, memoryStatus.Status);
            return MapToProto(memoryStatus);
        }

        // Приоритет 2: проверяем БД
        var dbStatus = await _dbContext.UsersOnlineStatuses
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (dbStatus != null)
        {
            _logger.LogTrace("User {UserId} status found in DB: {Status}", userId, dbStatus.Status);
            return MapToProto(dbStatus);
        }

        // Не найдено нигде - возвращаем Unknown
        _logger.LogTrace("User {UserId} status not found, returning Unknown", userId);

        return new UserOnlineStatus
        {
            UserId = userId,
            Status = ProtoStatusTypeId.Unknown,
            LastSeen = Timestamp.FromDateTime(DateTime.MinValue.ToUniversalTime())
        };
    }

    private static UserOnlineStatus BuildHiddenStatus(long userId)
    {
        return new UserOnlineStatus
        {
            UserId = userId,
            Status = ProtoStatusTypeId.Unknown,
            LastSeen = Timestamp.FromDateTime(DateTime.MinValue.ToUniversalTime())
        };
    }

    private static UserOnlineStatus MapToProto(Domain.Entities.UserOnlineStatus domainStatus)
    {
        return new UserOnlineStatus
        {
            UserId = domainStatus.UserId,
            Status = MapStatus(domainStatus.Status),
            LastSeen = Timestamp.FromDateTime(domainStatus.LastSeen.ToUniversalTime())
        };
    }

    private static ProtoStatusTypeId MapStatus(Domain.Enums.StatusTypeId domainStatus)
    {
        return domainStatus switch
        {
            Domain.Enums.StatusTypeId.Online => ProtoStatusTypeId.StatusOnline,
            Domain.Enums.StatusTypeId.Offline => ProtoStatusTypeId.StatusOffline,
            _ => ProtoStatusTypeId.Unknown
        };
    }
}
