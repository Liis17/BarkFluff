using System.Collections.Concurrent;
using Barkfluff.AdminPanel.Models;
using Microsoft.Extensions.Options;

namespace Barkfluff.AdminPanel.Services;

public class PendingAuthService
{
    private readonly ConcurrentDictionary<string, PendingAuthRequest> _requests = new();
    private readonly IOptions<AuthSettings> _settings;
    private readonly Timer _cleanupTimer;

    public PendingAuthService(IOptions<AuthSettings> settings)
    {
        _settings = settings;

        _cleanupTimer = new Timer(
            CleanupExpiredRequests,
            null,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(10));
    }

    public PendingAuthRequest CreateRequest(string? ipAddress, string? browser, string? os, string? userAgent, string? tokenName, string? nickname)
    {
        var request = new PendingAuthRequest
        {
            IpAddress = ipAddress,
            Browser = browser,
            Os = os,
            UserAgent = userAgent,
            TokenName = tokenName,
            Nickname = nickname
        };

        _requests[request.RequestId] = request;
        return request;
    }

    public PendingAuthRequest? GetRequest(string requestId)
    {
        return _requests.TryGetValue(requestId, out var request) ? request : null;
    }

    public bool UpdateRequestStatus(string requestId, AuthRequestStatus status, Guid? tokenId = null, long? approvedBy = null)
    {
        if (_requests.TryGetValue(requestId, out var request))
        {
            request.Status = status;
            request.TokenId = tokenId;
            request.ApprovedByTelegramUserId = approvedBy;
            request.CompletedAt = DateTime.UtcNow;

            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMinutes(5));
                _requests.TryRemove(requestId, out _);
            });

            return true;
        }
        return false;
    }

    public bool SetTelegramMessageId(string requestId, int messageId)
    {
        if (_requests.TryGetValue(requestId, out var request))
        {
            request.TelegramMessageId = messageId;
            return true;
        }
        return false;
    }

    public List<PendingAuthRequest> GetAllPending()
    {
        return _requests.Values
            .Where(r => r.Status == AuthRequestStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToList();
    }

    private void CleanupExpiredRequests(object? state)
    {
        var timeout = TimeSpan.FromMinutes(_settings.Value.PendingRequestTimeoutMinutes);
        var cutoff = DateTime.UtcNow - timeout;

        var expiredKeys = _requests
            .Where(kvp => kvp.Value.CreatedAt < cutoff && kvp.Value.Status == AuthRequestStatus.Pending)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            if (_requests.TryRemove(key, out var request))
            {
                request.Status = AuthRequestStatus.Expired;
            }
        }
    }
}
