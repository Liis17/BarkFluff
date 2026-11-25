using BarkFluff.Client.Core.Models;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.Proto.FastAuth;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

using System.Runtime.CompilerServices;
using System.Net.Http;
using System.Net.Sockets;
using BarkFluff.Shared.Exceptions.Identity;
using Grpc.Core;

namespace BarkFluff.Client.Core.Services;

public sealed partial class AuthenticationService : IAuthenticationService
{
    private readonly WebApiClient _webApi;
    private readonly IClientSession _session;
    private readonly ISecureSessionStore _secureSessionStore;

    public string? RestoreSessionErrorResourceKey { get; private set; }

    private readonly ChatListCache? _chatCache;
    private NodeConnection? _offlineConnection;
    public bool IsRestoredSessionOffline => _offlineConnection is { } connection &&
        ReferenceEquals(connection, _session.CurrentConnection);

    public AuthenticationService(WebApiClient webApi, IClientSession session, ISecureSessionStore secureSessionStore,
        ChatListCache? chatCache = null)
    {
        _webApi = webApi;
        _session = session;
        _secureSessionStore = secureSessionStore;
        _chatCache = chatCache;
    }

    public async Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        await _sessionCommitGate.WaitAsync(cancellationToken);
        try
        {
            return await RestoreSessionCoreAsync(cancellationToken);
        }
        finally
        {
            _sessionCommitGate.Release();
        }
    }

    private async Task<bool> RestoreSessionCoreAsync(CancellationToken cancellationToken)
    {
        RestoreSessionErrorResourceKey = null;
        var connection = _session.CurrentConnection;
        var saved = await _secureSessionStore.LoadAsync(cancellationToken);
        if (connection is null || saved is null || !ReferenceEquals(connection, _session.CurrentConnection))
        {
            return false;
        }

        var node = connection.Profile.BeaconAddress;
        var parameters = connection.ConnectionParameters;
        if (saved.Node is { } savedNode && !ChatListScope.SameNode(savedNode, node))
            return false;

        var loadingProfile = false;
        try
        {
            parameters.AccessToken = new BarkFluff.Proto.Identity.Token { Value = saved.AccessToken };
            parameters.RefreshToken = new BarkFluff.Proto.Identity.Token { Value = saved.RefreshToken };
            await _webApi.RefreshTokenForSessionRestoreAsync(parameters, cancellationToken);
            if (!IsCurrent() || parameters.AccessToken is not { Value.Length: > 0 } || !InitializeAuthorizedClient(parameters))
                return false;

            loadingProfile = true;
            var (_, user) = await _webApi.GetUserDataForSessionRestoreAsync(parameters, cancellationToken);
            loadingProfile = false;
            if (!IsCurrent() || user is not { Id: > 0 })
                return false;

            if (user.AccountStatus is BarkFluff.Proto.Users.AccountStatus.Blocked)
                throw new AccountBlockedException();
            if (user.AccountStatus is BarkFluff.Proto.Users.AccountStatus.PendingDeletion or BarkFluff.Proto.Users.AccountStatus.Deleted)
                throw new AccountPendingDeletionException();

            if (saved.UserId is > 0 && saved.UserId != user.Id)
                throw new InvalidRefreshTokenException();

            parameters.UserId = user.Id;
            parameters.UserName = user.Username;
            await PersistTokensAsync(cancellationToken);
            if (!IsCurrent())
                return false;
            _offlineConnection = null;
            return true;
        }
        catch (RpcException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            if (!IsCurrent())
                return false;

            var rejected = error is InvalidRefreshTokenException or AccountBlockedException or
                AccountPendingDeletionException or UserNotFoundException ||
                (loadingProfile && error is RpcException { StatusCode: StatusCode.Unauthenticated or StatusCode.PermissionDenied });
            if (rejected)
            {
                _offlineConnection = null;
                RestoreSessionErrorResourceKey = error switch
                {
                    AccountBlockedException => "Error_AccountBlocked",
                    AccountPendingDeletionException => "Error_AccountPendingDeletion",
                    _ => null
                };
                await _secureSessionStore.ClearAsync(cancellationToken);
                if (saved.UserId is { } rejectedUserId && _chatCache is not null)
                    await _chatCache.ClearAsync(ChatListScope.Create(node, rejectedUserId));
                return false;
            }

            // gRPC обычно сообщает об обрыве или таймауте статусом, но HTTP и сокеты могут передать сбой напрямую.
            var unavailable = error is RpcException { StatusCode: StatusCode.Unavailable or StatusCode.DeadlineExceeded }
                or HttpRequestException or SocketException;
            // Уже открытая офлайн-сессия остаётся доступной при неопределённом сбое повторной попытки.
            // Завершить её может подтверждённый отказ авторизации, обработанный выше.
            if ((unavailable || IsRestoredSessionOffline) && saved.Node is not null && saved.UserId is > 0)
            {
                parameters.UserId = saved.UserId.Value;
                parameters.UserName = saved.UserName ?? string.Empty;
                _offlineConnection = connection;
                return true;
            }

            return false;
        }

        bool IsCurrent() => !cancellationToken.IsCancellationRequested &&
            ReferenceEquals(connection, _session.CurrentConnection);
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
                result.Error.ErrorResourceKey ?? (requiresTwoFactor && !string.IsNullOrWhiteSpace(otpCode)
                    ? "Error_LoginTwoFactorInvalid"
                    : requiresTwoFactor ? "Error_LoginTwoFactorRequired" : "Error_LoginFailed"),
                requiresTwoFactor);
        }

        if (!ApplyTokens(result.accessToken, result.refreshToken))
        {
            return LoginResult.Failure("Error_LoginUnavailable");
        }

        _offlineConnection = null;
        await InitializeProfileAndPersistAsync(cancellationToken);
        return LoginResult.Success();
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
            : RegistrationStartResult.Failure(result.error.ErrorResourceKey ?? "Error_RegistrationFailed");
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
            return AuthenticationOperationResult.Failure(
                confirmation.error.ErrorResourceKey ?? "Error_RegistrationCodeInvalid");
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
            : PasswordResetStartResult.Failure(result.error.ErrorResourceKey ?? "Error_PasswordResetFailed");
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
            return AuthenticationOperationResult.Failure(
                confirmation.error.ErrorResourceKey ?? "Error_PasswordResetCodeInvalid");
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
            ? _secureSessionStore.SaveAsync(new StoredSession(access.Value, refresh.Value)
            {
                Node = _session.CurrentConnection?.Profile.BeaconAddress,
                UserId = parameters.UserId > 0 ? parameters.UserId : null,
                UserName = parameters.UserName
            }, cancellationToken)
            : Task.CompletedTask;
    }
}
