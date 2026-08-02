using BarkFluff.Client.Core.Models;

namespace BarkFluff.Client.Core.Services;

public interface IAuthenticationService
{
    Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    Task<LoginResult> LoginAsync(string loginOrEmail, string password, string otpCode, CancellationToken cancellationToken = default);

    Task<FastAuthSession?> CreateFastAuthSessionAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<FastAuthUpdate> SubscribeFastAuthAsync(FastAuthSession session, CancellationToken cancellationToken = default);

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
