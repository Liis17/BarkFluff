using System.Collections.Concurrent;

using BarkFluff.Proto.Users;

namespace BarkFluff.Messages.Features.Shared;

public sealed class UserAccountStatusCache
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(45);
    private readonly ConcurrentDictionary<long, CacheEntry> _statuses = new();
    private readonly ConcurrentDictionary<long, long> _versions = new();

    public async Task<AccountStatus> GetStatusAsync(
        long userId,
        UsersServerApi.UsersServerApiClient usersClient,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var version = _versions.GetOrAdd(userId, 0);
            if (_statuses.TryGetValue(userId, out var entry)
                && entry.Version == version
                && entry.ExpiresAt > DateTime.UtcNow)
                return entry.Status;

            var response = await usersClient.GetAccountRestrictionAsync(
                new GetAccountRestrictionRequest { UserId = userId },
                cancellationToken: cancellationToken);
            var status = response.Restriction.Status;
            if (_versions.GetOrAdd(userId, 0) != version)
                continue;

            _statuses[userId] = new CacheEntry(status, DateTime.UtcNow + TimeToLive, version);
            if (_versions.GetOrAdd(userId, 0) == version)
                return status;
        }
    }

    public void Invalidate(long userId)
    {
        _versions.AddOrUpdate(userId, 1, (_, version) => version + 1);
        _statuses.TryRemove(userId, out _);
    }

    private sealed record CacheEntry(AccountStatus Status, DateTime ExpiresAt, long Version);
}
