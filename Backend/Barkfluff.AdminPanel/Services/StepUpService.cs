using Barkfluff.AdminPanel.Models;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Barkfluff.AdminPanel.Services;

/// <summary>
/// In-memory registry of pending step-up confirmations.
/// A confirmation is single-use, tied to the session token, the action key and
/// a hash of the action's critical parameters, and expires shortly after approval.
/// </summary>
public class StepUpService : IDisposable
{
    public static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ApprovalValidFor = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, PendingStepUp> _requests = new();
    private readonly Timer _cleanupTimer;
    private readonly object _stateLock = new();

    public StepUpService()
    {
        _cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public static string ComputeParamsHash(string actionKey, string parameters)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{actionKey}|{parameters}"));
        return Convert.ToHexString(bytes);
    }

    public PendingStepUp CreateRequest(PendingStepUp request)
    {
        _requests[request.ConfirmationId] = request;
        return request;
    }

    public PendingStepUp? GetRequest(string confirmationId)
    {
        if (!_requests.TryGetValue(confirmationId, out var request))
            return null;

        ExpirePendingIfNeeded(request, DateTime.UtcNow);
        return request;
    }

    public bool SetTelegramMessageId(string confirmationId, int messageId)
    {
        if (_requests.TryGetValue(confirmationId, out var request))
        {
            request.TelegramMessageId = messageId;
            return true;
        }
        return false;
    }

    public bool Resolve(string confirmationId, StepUpStatus status, long approvedByTelegramUserId)
    {
        if (!_requests.TryGetValue(confirmationId, out var request))
            return false;

        lock (_stateLock)
        {
            if (request.Status != StepUpStatus.Pending)
                return false;

            if (status is not (StepUpStatus.Approved or StepUpStatus.Rejected))
                return false;

            if (request.TargetTelegramUserId != approvedByTelegramUserId)
                return false;

            if (DateTime.UtcNow - request.CreatedAt >= PendingTimeout)
            {
                request.Status = StepUpStatus.Expired;
                return false;
            }

            request.Status = status;
            request.ResolvedAt = DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// Atomically consumes an approved confirmation. Returns false when the
    /// confirmation is unknown, not approved, expired, already used, or bound
    /// to a different session/action/params.
    /// </summary>
    public bool TryConsume(string confirmationId, Guid tokenId, string actionKey, string parameters)
    {
        lock (_stateLock)
        {
            if (!_requests.TryGetValue(confirmationId, out var request))
                return false;

            if (request.Status != StepUpStatus.Approved)
                return false;

            if (request.TokenId != tokenId)
                return false;

            if (!string.Equals(request.ActionKey, actionKey, StringComparison.Ordinal))
                return false;

            if (!string.Equals(ComputeParamsHash(request.ActionKey, request.Params), ComputeParamsHash(actionKey, parameters), StringComparison.Ordinal))
                return false;

            if (request.ResolvedAt is not { } resolvedAt || DateTime.UtcNow - resolvedAt > ApprovalValidFor)
            {
                request.Status = StepUpStatus.Expired;
                return false;
            }

            request.Status = StepUpStatus.Used;
            return true;
        }
    }

    private void CleanupExpired(object? state)
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _requests)
        {
            lock (_stateLock)
            {
                var request = kvp.Value;
                if (request.Status == StepUpStatus.Pending && now - request.CreatedAt >= PendingTimeout)
                    request.Status = StepUpStatus.Expired;

                var lastActivity = request.ResolvedAt ?? request.CreatedAt;
                if (request.Status != StepUpStatus.Pending && now - lastActivity >= PendingTimeout)
                    _requests.TryRemove(kvp.Key, out _);
            }
        }
    }

    private void ExpirePendingIfNeeded(PendingStepUp request, DateTime now)
    {
        if (request.Status != StepUpStatus.Pending || now - request.CreatedAt < PendingTimeout)
            return;

        lock (_stateLock)
        {
            if (request.Status == StepUpStatus.Pending && now - request.CreatedAt >= PendingTimeout)
                request.Status = StepUpStatus.Expired;
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
