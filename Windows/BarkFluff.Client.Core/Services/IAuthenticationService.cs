using BarkFluff.Client.Core.Models;

namespace BarkFluff.Client.Core.Services;

public interface IAuthenticationService
{
    Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    AuthenticationAttempt? CreateAuthenticationAttempt();

    bool IsCurrentAuthenticationAttempt(AuthenticationAttempt attempt);

    Task CancelAuthenticationAttemptAsync(AuthenticationAttempt attempt);

    Task<SignInResolutionResult> ResolveSignInAsync(
        AuthenticationAttempt attempt,
        string login,
        CancellationToken cancellationToken = default);

    Task<SignInChallengeResult> BeginSignInAsync(
        AuthenticationAttempt attempt,
        string login,
        string password,
        BarkFluff.Proto.Identity.AuthLoginMode loginMode,
        BarkFluff.Proto.Identity.OtpTypeId factor,
        bool useRecoveryCode,
        CancellationToken cancellationToken = default);

    Task<SignInChallengeResult> GetAuthChallengeAsync(
        AuthenticationAttempt attempt,
        BarkFluff.Proto.Identity.AuthChallengeReference reference,
        CancellationToken cancellationToken = default);

    Task<AuthenticationOperationResult> CompleteAuthChallengeAsync(
        AuthenticationAttempt attempt,
        BarkFluff.Proto.Identity.AuthChallengeReference reference,
        string code,
        bool useRecoveryCode,
        CancellationToken cancellationToken = default);

    Task<SignInChallengeResult> ResendAuthChallengeAsync(
        AuthenticationAttempt attempt,
        BarkFluff.Proto.Identity.AuthChallengeReference reference,
        CancellationToken cancellationToken = default);

    Task<AuthenticationOperationResult> CancelAuthChallengeAsync(
        AuthenticationAttempt attempt,
        BarkFluff.Proto.Identity.AuthChallengeReference reference,
        CancellationToken cancellationToken = default);

    Task<FastAuthSession?> CreateFastAuthSessionAsync(
        AuthenticationAttempt attempt,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<FastAuthUpdate> SubscribeFastAuthAsync(
        AuthenticationAttempt attempt,
        FastAuthSession session,
        CancellationToken cancellationToken = default);

    Task<LoginResult> LoginAsync(string loginOrEmail, string password, string otpCode, CancellationToken cancellationToken = default);

    Task<RegistrationStartResult> StartRegistrationAsync(
        string firstName,
        string lastName,
        string username,
        string email,
        CancellationToken cancellationToken = default);

    Task<AuthenticationOperationResult> ConfirmRegistrationAsync(
        string codeId,
        string code,
        CancellationToken cancellationToken = default);

    Task<AuthenticationOperationResult> SetPasswordAsync(string password, CancellationToken cancellationToken = default);

    Task<PasswordResetStartResult> StartPasswordResetAsync(string loginOrEmail, CancellationToken cancellationToken = default);

    Task<AuthenticationOperationResult> CompletePasswordResetAsync(
        string resetId,
        string code,
        string password,
        CancellationToken cancellationToken = default);
}
