namespace BarkFluff.Identity.Security;

public interface IIdentityAbuseGuard
{
    Task EnsureRequestAllowedAsync(
        IdentityAbuseOperation operation,
        string? trustedIpAddress,
        string? subject,
        bool countSubject,
        CancellationToken cancellationToken = default);

    Task EnsureSubjectRequestAllowedAsync(
        IdentityAbuseOperation operation,
        string subject,
        CancellationToken cancellationToken = default);

    Task EnsureUserAllowedAsync(long userId, CancellationToken cancellationToken = default);

    Task EnsureLoginAllowedAsync(
        string login,
        string? trustedIpAddress,
        CancellationToken cancellationToken = default);

    Task<IdentityFailureResult> RegisterLoginFailureAsync(
        string login,
        string? trustedIpAddress,
        long? userId,
        CancellationToken cancellationToken = default);

    Task ClearLoginFailuresAsync(
        string login,
        string? trustedIpAddress,
        long userId,
        CancellationToken cancellationToken = default);

    Task EnsureCodeAllowedAsync(
        IdentityCodeKind codeKind,
        Guid codeId,
        CancellationToken cancellationToken = default);

    Task<IdentityFailureResult> RegisterCodeFailureAsync(
        IdentityCodeKind codeKind,
        Guid codeId,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);

    Task ClearCodeFailuresAsync(
        IdentityCodeKind codeKind,
        Guid codeId,
        CancellationToken cancellationToken = default);

    Task EnsureOtpOperationAllowedAsync(
        IdentityOtpOperation operation,
        long userId,
        CancellationToken cancellationToken = default);

    Task<IdentityFailureResult> RegisterOtpFailureAsync(
        IdentityOtpOperation operation,
        long userId,
        CancellationToken cancellationToken = default);

    Task ClearOtpFailuresAsync(
        IdentityOtpOperation operation,
        long userId,
        CancellationToken cancellationToken = default);

    Task DelayAfterFailureAsync(int attempts, CancellationToken cancellationToken = default);
}
