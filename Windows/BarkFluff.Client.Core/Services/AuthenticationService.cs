using BarkFluff.Client.Core.Models;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.Proto.FastAuth;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

using System.Runtime.CompilerServices;

namespace BarkFluff.Client.Core.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly WebApiClient _webApi;
    private readonly IClientSession _session;
    private readonly ISecureSessionStore _secureSessionStore;

    public AuthenticationService(WebApiClient webApi, IClientSession session, ISecureSessionStore secureSessionStore)
    {
        _webApi = webApi;
        _session = session;
        _secureSessionStore = secureSessionStore;
    }

    public async Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var parameters = GetConnectionParameters();
        var saved = await _secureSessionStore.LoadAsync(cancellationToken);
        if (parameters is null || saved is null)
        {
            return false;
        }

        parameters.AccessToken = new BarkFluff.Proto.Identity.Token { Value = saved.AccessToken };
        parameters.RefreshToken = new BarkFluff.Proto.Identity.Token { Value = saved.RefreshToken };
        var refreshed = await _webApi.ForceRefreshTokenAsync(parameters);
        if (!refreshed.IsSuccess || parameters.AccessToken is null || !InitializeAuthorizedClient(parameters))
        {
            await _secureSessionStore.ClearAsync(cancellationToken);
            return false;
        }

        var profile = await _webApi.GetUserData(parameters);
        if (!profile.Error.IsSuccess || profile.Data is null)
        {
            await _secureSessionStore.ClearAsync(cancellationToken);
            return false;
        }

        parameters.UserId = profile.Data.Id;
        parameters.UserName = profile.Data.Username;
        await PersistTokensAsync(cancellationToken);
        return true;
    }

    public async Task<LoginResult> LoginAsync(
        string loginOrEmail,
        string password,
        string otpCode,
        CancellationToken cancellationToken = default)
    {
        var connection = _session.CurrentConnection;
        if (connection is null)
        {
            return LoginResult.Failure("Error_LoginUnavailable");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var isEmail = loginOrEmail.Contains('@');
        var result = await _webApi.Authorizations(
            isEmail ? loginOrEmail : string.Empty,
            isEmail ? string.Empty : loginOrEmail,
            password,
            otpCode,
            connection.ConnectionParameters);

        if (!result.Error.IsSuccess || result.accessToken is null || result.refreshToken is null)
        {
            var requiresTwoFactor = result.getMeOtpCode;
            return LoginResult.Failure(
                requiresTwoFactor && !string.IsNullOrWhiteSpace(otpCode)
                    ? "Error_LoginTwoFactorInvalid"
                    : requiresTwoFactor ? "Error_LoginTwoFactorRequired" : "Error_LoginFailed",
                requiresTwoFactor);
        }

        if (!ApplyTokens(result.accessToken, result.refreshToken))
        {
            return LoginResult.Failure("Error_LoginUnavailable");
        }

        await InitializeProfileAndPersistAsync(cancellationToken);
        return LoginResult.Success();
    }

    public async Task<FastAuthSession?> CreateFastAuthSessionAsync(CancellationToken cancellationToken = default)
    {
        var connection = _session.CurrentConnection;
        if (connection is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var initialized = _webApi.CreateFastAuthClient(
            connection.ConnectionParameters,
            Environment.MachineName,
            ClientMetadata.OperatingSystem,
            ClientMetadata.AppName,
            ClientMetadata.AppVersion,
            string.Empty);
        if (!initialized.IsSuccess)
        {
            return null;
        }

        var result = await _webApi.GenerateFastAuthToken(TokenFormat.Qr);
        if (!result.Item1.IsSuccess || result.Item2 is not { Token.Value: { Length: > 0 } base64Png, FastAuthId: { Length: > 0 } fastAuthId })
        {
            return null;
        }

        return new FastAuthSession(fastAuthId, new FastAuthQrCode(base64Png, result.Item2.ExpiresAt.ToDateTimeOffset()));
    }

    public async IAsyncEnumerable<FastAuthUpdate> SubscribeFastAuthAsync(
        FastAuthSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subscription = await _webApi.SubscribeFastAuthResult(session.Id, cancellationToken);
        if (!subscription.Item1.IsSuccess || subscription.Item2 is null)
        {
            yield return new FastAuthUpdate(FastAuthUpdateKind.Failed, "Error_FastAuthUnavailable");
            yield break;
        }

        await foreach (var result in subscription.Item2.WithCancellation(cancellationToken))
        {
            switch (result.Status)
            {
                case FastAuthStatus.Scanned:
                    yield return new FastAuthUpdate(FastAuthUpdateKind.Scanned);
                    break;
                case FastAuthStatus.Accepted:
                    if (string.IsNullOrWhiteSpace(result.AccessToken) || string.IsNullOrWhiteSpace(result.RefreshToken))
                    {
                        yield return new FastAuthUpdate(FastAuthUpdateKind.Failed, "Error_FastAuthFailed");
                        yield break;
                    }

                    if (ApplyTokens(
                        new BarkFluff.Proto.Identity.Token
                        {
                            Value = result.AccessToken,
                            ExpirationDate = result.AccessTokenExpiresAt
                        },
                        new BarkFluff.Proto.Identity.Token
                        {
                            Value = result.RefreshToken,
                            ExpirationDate = result.RefreshTokenExpiresAt
                        }))
                    {
                        await InitializeProfileAndPersistAsync(cancellationToken);
                        yield return new FastAuthUpdate(FastAuthUpdateKind.Accepted);
                    }
                    else
                    {
                        yield return new FastAuthUpdate(FastAuthUpdateKind.Failed, "Error_LoginUnavailable");
                    }
                    yield break;
                case FastAuthStatus.Rejected:
                    yield return new FastAuthUpdate(FastAuthUpdateKind.Rejected);
                    yield break;
                case FastAuthStatus.Expired:
                    yield return new FastAuthUpdate(FastAuthUpdateKind.Expired);
                    yield break;
            }
        }

        yield return new FastAuthUpdate(FastAuthUpdateKind.Failed, "Error_FastAuthUnavailable");
    }

    public async Task<RegistrationStartResult> StartRegistrationAsync(
        string firstName,
        string lastName,
        string username,
        string email,
        CancellationToken cancellationToken = default)
    {
        var parameters = GetConnectionParameters();
        if (parameters is null)
        {
            return RegistrationStartResult.Failure("Error_LoginUnavailable");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await _webApi.CreateAccount(firstName, lastName, email, username, parameters);
        return result.error.IsSuccess && !string.IsNullOrWhiteSpace(result.userid)
            ? RegistrationStartResult.Success(result.userid)
            : RegistrationStartResult.Failure("Error_RegistrationFailed");
    }

    public async Task<AuthenticationOperationResult> ConfirmRegistrationAsync(
        string codeId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var parameters = GetConnectionParameters();
        if (parameters is null)
        {
            return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var confirmation = await _webApi.ConfirmAccount(codeId, code, parameters);
        if (!confirmation.error.IsSuccess || confirmation.RefreshToken is null)
        {
            return AuthenticationOperationResult.Failure("Error_RegistrationCodeInvalid");
        }

        return await ApplyRefreshTokenAsync(parameters, confirmation.RefreshToken);
    }

    public async Task<AuthenticationOperationResult> SetPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        var parameters = GetConnectionParameters();
        if (parameters is null)
        {
            return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await _webApi.SetPassword(password, parameters);
        return result.IsSuccess
            ? AuthenticationOperationResult.Success()
            : AuthenticationOperationResult.Failure("Error_PasswordInvalid");
    }

    public async Task<PasswordResetStartResult> StartPasswordResetAsync(
        string loginOrEmail,
        CancellationToken cancellationToken = default)
    {
        var parameters = GetConnectionParameters();
        if (parameters is null)
        {
            return PasswordResetStartResult.Failure("Error_LoginUnavailable");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var isEmail = loginOrEmail.Contains('@');
        var result = await _webApi.ResetPassword(
            isEmail ? loginOrEmail : string.Empty,
            isEmail ? string.Empty : loginOrEmail,
            parameters);
        return result.error.IsSuccess && !string.IsNullOrWhiteSpace(result.resetId)
            ? PasswordResetStartResult.Success(result.resetId)
            : PasswordResetStartResult.Failure("Error_PasswordResetFailed");
    }

    public async Task<AuthenticationOperationResult> CompletePasswordResetAsync(
        string resetId,
        string code,
        string password,
        CancellationToken cancellationToken = default)
    {
        var parameters = GetConnectionParameters();
        if (parameters is null)
        {
            return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var confirmation = await _webApi.ConfirmResetCode(resetId, code, parameters);
        if (!confirmation.error.IsSuccess || confirmation.refreshToken is null)
        {
            return AuthenticationOperationResult.Failure("Error_PasswordResetCodeInvalid");
        }

        var tokenResult = await ApplyRefreshTokenAsync(parameters, confirmation.refreshToken);
        return tokenResult.IsSuccess
            ? await SetPasswordAsync(password, cancellationToken)
            : tokenResult;
    }

    private GlobalParam? GetConnectionParameters() => _session.CurrentConnection?.ConnectionParameters;

    private async Task<AuthenticationOperationResult> ApplyRefreshTokenAsync(
        GlobalParam parameters,
        BarkFluff.Proto.Identity.Token refreshToken)
    {
        parameters.RefreshToken = refreshToken;
        var access = await _webApi.ForceRefreshTokenAsync(parameters);
        if (!access.IsSuccess || parameters.AccessToken is null)
        {
            return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
        }

        if (!InitializeAuthorizedClient(parameters))
        {
            return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
        }

        await PersistTokensAsync();
        return AuthenticationOperationResult.Success();
    }

    private bool ApplyTokens(BarkFluff.Proto.Identity.Token accessToken, BarkFluff.Proto.Identity.Token refreshToken)
    {
        var parameters = GetConnectionParameters();
        if (parameters is null)
        {
            return false;
        }

        parameters.AccessToken = accessToken;
        parameters.RefreshToken = refreshToken;
        return InitializeAuthorizedClient(parameters);
    }

    private bool InitializeAuthorizedClient(GlobalParam parameters) => _webApi.CreateAC(
            parameters,
            Environment.MachineName,
            ClientMetadata.OperatingSystem,
            ClientMetadata.AppName,
            ClientMetadata.AppVersion,
            string.Empty).IsSuccess;

    private async Task InitializeProfileAndPersistAsync(CancellationToken cancellationToken)
    {
        var parameters = GetConnectionParameters();
        if (parameters is not null)
        {
            var profile = await _webApi.GetUserData(parameters);
            if (profile.Error.IsSuccess && profile.Data is not null)
            {
                parameters.UserId = profile.Data.Id;
                parameters.UserName = profile.Data.Username;
            }
        }

        await PersistTokensAsync(cancellationToken);
    }

    private Task PersistTokensAsync(CancellationToken cancellationToken = default)
    {
        var parameters = GetConnectionParameters();
        return parameters?.AccessToken is { Value.Length: > 0 } access && parameters.RefreshToken is { Value.Length: > 0 } refresh
            ? _secureSessionStore.SaveAsync(new StoredSession(access.Value, refresh.Value), cancellationToken)
            : Task.CompletedTask;
    }
}
