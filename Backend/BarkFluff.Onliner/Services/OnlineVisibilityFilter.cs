using BarkFluff.Proto.Users;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Фильтрует списки пользователей по настройке OnlineVisibility.
/// FRIENDS трактуется как NONE, пока нет сервиса отношений.
/// </summary>
public class OnlineVisibilityFilter
{
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<OnlineVisibilityFilter> _logger;

    public OnlineVisibilityFilter(
        UsersServerApi.UsersServerApiClient usersClient,
        ILogger<OnlineVisibilityFilter> logger)
    {
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<HashSet<long>> GetVisibleUserIdsAsync(
        IEnumerable<long> targetUserIds,
        long callerUserId,
        CancellationToken cancellationToken = default)
    {
        var visible = new HashSet<long>();

        foreach (var targetId in targetUserIds.Distinct())
        {
            if (targetId == callerUserId)
            {
                visible.Add(targetId);
                continue;
            }

            if (await IsVisibleToCaller(targetId, cancellationToken))
            {
                visible.Add(targetId);
            }
        }

        return visible;
    }

    public async Task<bool> IsVisibleToCaller(long targetUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _usersClient.GetUserPrivacyAsync(
                new GetUserPrivacyRequest { UserId = targetUserId },
                cancellationToken: cancellationToken);

            // FRIENDS == NONE пока нет сервиса отношений.
            // TODO: активировать FRIENDS, когда появится сервис отношений.
            return response.Settings.OnlineVisibility == ProfileFieldVisibility.All;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch privacy for user {UserId}, defaulting to visible",
                targetUserId);
            return true;
        }
    }
}
