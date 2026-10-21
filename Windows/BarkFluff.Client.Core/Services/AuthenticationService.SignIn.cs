using System.Runtime.CompilerServices;
using System.Text.Json;

using BarkFluff.Client.Core.Models;
using BarkFluff.Proto.FastAuth;
using BarkFluff.WebApi.Core.MessengerData;
using Grpc.Core;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public sealed partial class AuthenticationService
{
    private static readonly TimeSpan AttemptRetirementTimeout = TimeSpan.FromSeconds(3);

    private readonly object _attemptSync = new();
    private readonly SemaphoreSlim _sessionCommitGate = new(1, 1);
    private AuthenticationAttemptState? _activeAttempt;
    private long _attemptGeneration;

    public AuthenticationAttempt? CreateAuthenticationAttempt()
    {
        var connection = _session.CurrentConnection;
        if (connection is null)
        {
            return null;
        }

        AuthenticationAttemptState? previous;
        long generation;
        lock (_attemptSync)
        {
            previous = _activeAttempt;
            _activeAttempt = null;
            generation = ++_attemptGeneration;
        }

        if (previous is not null)
        {
            previous.Cancel();
            _ = RetireAttemptAsync(previous);
        }

        WebApiClient? attemptWebApi = null;
        try
        {
            var parameters = SnapshotConnectionParameters(connection.ConnectionParameters);
            attemptWebApi = new WebApiClient();
            var initialized = attemptWebApi.CreatePreLoginAuthenticationClient(
                parameters,
                Environment.MachineName,
                ClientMetadata.OperatingSystem,
                ClientMetadata.AppName,
                ClientMetadata.AppVersion,
                string.Empty);
            if (!initialized.IsSuccess)
            {
                attemptWebApi.Dispose();
                return null;
            }

            var state = new AuthenticationAttemptState(
                AuthenticationAttempt.Create(),
                connection,
                parameters,
                attemptWebApi,
                generation);

            lock (_attemptSync)
            {
                if (generation != _attemptGeneration || !ReferenceEquals(_session.CurrentConnection, connection))
                {
                    state.Cancel();
                    _ = RetireAttemptAsync(state);
                    return null;
                }

                _activeAttempt = state;
            }

            return state.Handle;
        }
        catch (Exception)
        {
            attemptWebApi?.Dispose();
            return null;
        }
    }

    public bool IsCurrentAuthenticationAttempt(AuthenticationAttempt attempt) =>
        TryGetCurrentAttempt(attempt, out _);

    public async Task CancelAuthenticationAttemptAsync(AuthenticationAttempt attempt)
    {
        AuthenticationAttemptState? state;
        lock (_attemptSync)
        {
            state = _activeAttempt?.Handle == attempt ? _activeAttempt : null;
            if (state is not null)
            {
                _activeAttempt = null;
                _attemptGeneration++;
            }
        }

        if (state is null)
        {
            return;
        }

        state.Cancel();
        await RetireAttemptAsync(state);
    }

    public async Task<SignInResolutionResult> ResolveSignInAsync(
        AuthenticationAttempt attempt,
        string login,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentAttempt(attempt, out var state))
        {
            return SignInResolutionResult.Failure(null, "Error_LoginUnavailable");
        }

        try
        {
            using var operation = state.EnterOperation();
            using var linked = LinkCancellation(state, cancellationToken);
            var response = await state.WebApi.ResolveSignIn(login, linked.Token);
            if (!IsCurrentState(state))
            {
                return SignInResolutionResult.Failure(null, "Error_LoginUnavailable");
            }

            if (!string.IsNullOrWhiteSpace(response.ErrorCode))
            {
                return SignInResolutionResult.Failure(response.ErrorCode, ErrorKeyForCode(response.ErrorCode));
            }

            return response.LoginMode is BarkFluff.Proto.Identity.AuthLoginMode.Password
                or BarkFluff.Proto.Identity.AuthLoginMode.PasswordSecondFactor
                or BarkFluff.Proto.Identity.AuthLoginMode.TelegramLogin
                    ? SignInResolutionResult.Success(response.LoginMode)
                    : SignInResolutionResult.Failure("account_not_found", "Error_LoginAccountNotFound");
        }
        catch (OperationCanceledException) when (IsCancellationRequested(state, cancellationToken))
        {
            throw;
        }
        catch (Exception exception)
        {
            return SignInResolutionResult.Failure(null, ErrorKeyForException(exception));
        }
    }

    public async Task<SignInChallengeResult> BeginSignInAsync(
        AuthenticationAttempt attempt,
        string login,
        string password,
        BarkFluff.Proto.Identity.AuthLoginMode loginMode,
        BarkFluff.Proto.Identity.OtpTypeId factor,
        bool useRecoveryCode,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentAttempt(attempt, out var state))
        {
            return SignInChallengeResult.Failure(null, "Error_LoginUnavailable");
        }

        try
        {
            using var operation = state.EnterOperation();
            using var linked = LinkCancellation(state, cancellationToken);
            var response = await state.WebApi.BeginSignIn(new BarkFluff.Proto.Identity.BeginSignInRequest
            {
                Login = login,
                Password = password,
                LoginMode = loginMode,
                Factor = factor,
                UseRecoveryCode = useRecoveryCode,
                ReportLoginModeMismatch = true
            }, linked.Token);

            var result = MapChallenge(response);
            if (result.Challenge is not null)
            {
                state.SetChallenge(result.Challenge.Reference);
                if (!IsCurrentState(state))
                {
                    await TryCancelChallengeOnRetiredAttemptAsync(state, result.Challenge.Reference);
                    return SignInChallengeResult.Failure(null, "Error_LoginUnavailable");
                }
            }

            return result;
        }
        catch (OperationCanceledException) when (IsCancellationRequested(state, cancellationToken))
        {
            throw;
        }
        catch (Exception exception)
        {
            return SignInChallengeResult.Failure(null, ErrorKeyForException(exception));
        }
    }

    public async Task<SignInChallengeResult> GetAuthChallengeAsync(
        AuthenticationAttempt attempt,
        BarkFluff.Proto.Identity.AuthChallengeReference reference,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentAttempt(attempt, out var state))
        {
            return SignInChallengeResult.Failure(null, "Error_LoginUnavailable");
        }

        try
        {
            using var operation = state.EnterOperation();
            using var linked = LinkCancellation(state, cancellationToken);
            var response = await state.WebApi.GetAuthChallenge(reference, linked.Token);
            var result = MapChallenge(response);
            if (result.Challenge is not null)
            {
                state.SetChallenge(result.Challenge.Reference);
            }

            return IsCurrentState(state)
                ? result
                : SignInChallengeResult.Failure(null, "Error_LoginUnavailable");
        }
        catch (OperationCanceledException) when (IsCancellationRequested(state, cancellationToken))
        {
            throw;
        }
        catch (Exception exception)
        {
            return SignInChallengeResult.Failure(null, ErrorKeyForException(exception));
        }
    }

    public async Task<AuthenticationOperationResult> CompleteAuthChallengeAsync(
        AuthenticationAttempt attempt,
        BarkFluff.Proto.Identity.AuthChallengeReference reference,
        string code,
        bool useRecoveryCode,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentAttempt(attempt, out var state))
        {
            return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
        }

        if (state.PendingAccessToken is null || state.PendingRefreshToken is null)
        {
            try
            {
                using var operation = state.EnterOperation();
                using var linked = LinkCancellation(state, cancellationToken);
                var response = await state.WebApi.CompleteAuthChallenge(new BarkFluff.Proto.Identity.CompleteAuthChallengeRequest
                {
                    Challenge = CopyReference(reference),
                    Code = code,
                    UseRecoveryCode = useRecoveryCode
                }, linked.Token);

                if (!string.IsNullOrWhiteSpace(response.ErrorCode))
                {
                    return AuthenticationOperationResult.Failure(ErrorKeyForCode(response.ErrorCode));
                }

                if (response.State != BarkFluff.Proto.Identity.AuthChallengeState.Completed || response.Session is null)
                {
                    return AuthenticationOperationResult.Failure(response.State switch
                    {
                        BarkFluff.Proto.Identity.AuthChallengeState.Expired => "Error_LoginChallengeExpired",
                        BarkFluff.Proto.Identity.AuthChallengeState.Rejected => "Error_LoginChallengeRejected",
                        BarkFluff.Proto.Identity.AuthChallengeState.Cancelled => "Error_LoginChallengeCancelled",
                        _ => "Error_LoginFailed"
                    });
                }

                if (response.Session.AccessToken is not { Value.Length: > 0 } accessToken ||
                    response.Session.RefreshToken is not { Value.Length: > 0 } refreshToken)
                {
                    return AuthenticationOperationResult.Failure("Error_LoginMissingTokens");
                }

                state.SetPendingTokens(accessToken, refreshToken);
            }
            catch (OperationCanceledException) when (IsCancellationRequested(state, cancellationToken))
            {
                throw;
            }
            catch (Exception exception)
            {
                return AuthenticationOperationResult.Failure(ErrorKeyForException(exception));
            }
        }

        var result = await CompleteAuthenticationSessionAsync(state, cancellationToken);
        if (result.IsSuccess)
        {
            FinishSuccessfulAttempt(state);
            await RetireAttemptAsync(state);
        }

        return result;
    }

    public async Task<SignInChallengeResult> ResendAuthChallengeAsync(
        AuthenticationAttempt attempt,
        BarkFluff.Proto.Identity.AuthChallengeReference reference,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentAttempt(attempt, out var state))
        {
            return SignInChallengeResult.Failure(null, "Error_LoginUnavailable");
        }

        try
        {
            using var operation = state.EnterOperation();
            using var linked = LinkCancellation(state, cancellationToken);
            var response = await state.WebApi.ResendAuthChallenge(reference, linked.Token);
            var result = MapChallenge(response);
            if (result.Challenge is not null)
            {
                state.SetChallenge(result.Challenge.Reference);
            }

            return IsCurrentState(state)
                ? result
                : SignInChallengeResult.Failure(null, "Error_LoginUnavailable");
        }
        catch (OperationCanceledException) when (IsCancellationRequested(state, cancellationToken))
        {
            throw;
        }
        catch (Exception exception)
        {
            return SignInChallengeResult.Failure(null, ErrorKeyForException(exception));
        }
    }

    public async Task<AuthenticationOperationResult> CancelAuthChallengeAsync(
        AuthenticationAttempt attempt,
        BarkFluff.Proto.Identity.AuthChallengeReference reference,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentAttempt(attempt, out var state))
        {
            return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
        }

        try
        {
            using var operation = state.EnterOperation();
            using var linked = LinkCancellation(state, cancellationToken);
            var response = await state.WebApi.CancelAuthChallenge(reference, linked.Token);
            if (!IsCurrentState(state))
            {
                return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
            }

            if (!string.IsNullOrWhiteSpace(response.ErrorCode))
            {
                return AuthenticationOperationResult.Failure(ErrorKeyForCode(response.ErrorCode));
            }

            if (response.State == BarkFluff.Proto.Identity.AuthChallengeState.Cancelled)
            {
                state.ClearChallenge();
            }

            return AuthenticationOperationResult.Success();
        }
        catch (OperationCanceledException) when (IsCancellationRequested(state, cancellationToken))
        {
            throw;
        }
        catch (Exception exception)
        {
            return AuthenticationOperationResult.Failure(ErrorKeyForException(exception));
        }
    }

    public async Task<FastAuthSession?> CreateFastAuthSessionAsync(
        AuthenticationAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentAttempt(attempt, out var state))
        {
            return null;
        }

        try
        {
            using var operation = state.EnterOperation();
            using var linked = LinkCancellation(state, cancellationToken);
            var initialized = state.WebApi.CreateFastAuthClient(
                state.Parameters,
                Environment.MachineName,
                ClientMetadata.OperatingSystem,
                ClientMetadata.AppName,
                ClientMetadata.AppVersion,
                string.Empty);
            if (!initialized.IsSuccess)
            {
                return null;
            }

            var result = await state.WebApi.GenerateFastAuthToken(TokenFormat.Qr, linked.Token);
            if (!result.Item1.IsSuccess || result.Item2 is not
                { Token.Value: { Length: > 0 } base64Png, FastAuthId: { Length: > 0 } fastAuthId })
            {
                return null;
            }

            return new FastAuthSession(fastAuthId, new FastAuthQrCode(base64Png, result.Item2.ExpiresAt.ToDateTimeOffset()));
        }
        catch (OperationCanceledException) when (IsCancellationRequested(state, cancellationToken))
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async IAsyncEnumerable<FastAuthUpdate> SubscribeFastAuthAsync(
        AuthenticationAttempt attempt,
        FastAuthSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentAttempt(attempt, out var state))
        {
            yield return new FastAuthUpdate(FastAuthUpdateKind.Failed, "Error_FastAuthUnavailable");
            yield break;
        }

        using var operation = state.EnterOperation();
        using var linked = LinkCancellation(state, cancellationToken);
        try
        {
            var subscription = await state.WebApi.SubscribeFastAuthResult(session.Id, linked.Token);
            if (!subscription.Item1.IsSuccess || subscription.Item2 is null)
            {
                yield return new FastAuthUpdate(FastAuthUpdateKind.Failed, "Error_FastAuthUnavailable");
                yield break;
            }

            await foreach (var update in subscription.Item2.WithCancellation(linked.Token))
            {
                if (!IsCurrentState(state))
                {
                    yield return new FastAuthUpdate(FastAuthUpdateKind.Failed, "Error_FastAuthUnavailable");
                    yield break;
                }

                switch (update.Status)
                {
                    case FastAuthStatus.Scanned:
                        yield return new FastAuthUpdate(FastAuthUpdateKind.Scanned);
                        break;
                    case FastAuthStatus.Accepted:
                        if (string.IsNullOrWhiteSpace(update.AccessToken) || string.IsNullOrWhiteSpace(update.RefreshToken))
                        {
                            yield return new FastAuthUpdate(FastAuthUpdateKind.Failed, "Error_FastAuthFailed");
                            yield break;
                        }

                        state.SetPendingTokens(
                            new BarkFluff.Proto.Identity.Token { Value = update.AccessToken, ExpirationDate = update.AccessTokenExpiresAt },
                            new BarkFluff.Proto.Identity.Token { Value = update.RefreshToken, ExpirationDate = update.RefreshTokenExpiresAt });
                        var accepted = await CompleteAuthenticationSessionAsync(state, linked.Token);
                        if (!accepted.IsSuccess)
                        {
                            yield return new FastAuthUpdate(FastAuthUpdateKind.Failed, accepted.ErrorResourceKey ?? "Error_LoginUnavailable");
                            yield break;
                        }

                        FinishSuccessfulAttempt(state);
                        yield return new FastAuthUpdate(FastAuthUpdateKind.Accepted);
                        yield break;
                    case FastAuthStatus.Rejected:
                        yield return new FastAuthUpdate(FastAuthUpdateKind.Rejected);
                        yield break;
                    case FastAuthStatus.Expired:
                        yield return new FastAuthUpdate(FastAuthUpdateKind.Expired);
                        yield break;
                }
            }

            if (!linked.IsCancellationRequested && IsCurrentState(state))
            {
                yield return new FastAuthUpdate(FastAuthUpdateKind.Failed, "Error_FastAuthUnavailable");
            }
        }
        finally
        {
            if (state.IsCompleted)
            {
                _ = RetireAttemptAsync(state);
            }
        }
    }

    private async Task<AuthenticationOperationResult> CompleteAuthenticationSessionAsync(
        AuthenticationAttemptState state,
        CancellationToken cancellationToken)
    {
        using var operation = state.EnterOperation();
        using var linked = LinkCancellation(state, cancellationToken);

        if (state.PendingAccessToken is not { Value.Length: > 0 } accessToken ||
            state.PendingRefreshToken is not { Value.Length: > 0 } refreshToken)
        {
            return AuthenticationOperationResult.Failure("Error_LoginMissingTokens");
        }

        if (!IsCurrentState(state))
        {
            return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
        }

        try
        {
            state.Parameters.AccessToken = CopyToken(accessToken);
            state.Parameters.RefreshToken = CopyToken(refreshToken);
            if (!state.AuthorizedClientInitialized)
            {
                var initialized = state.WebApi.CreateAC(
                    state.Parameters,
                    Environment.MachineName,
                    ClientMetadata.OperatingSystem,
                    ClientMetadata.AppName,
                    ClientMetadata.AppVersion,
                    string.Empty);
                if (!initialized.IsSuccess)
                {
                    return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
                }

                state.AuthorizedClientInitialized = true;
            }

            var profile = await state.WebApi.GetUserDataForAuthentication(state.Parameters, linked.Token);
            if (!profile.Error.IsSuccess || profile.Data is null)
            {
                return AuthenticationOperationResult.Failure(profile.Error.ErrorResourceKey ?? "Error_LoginUnavailable");
            }

            state.Parameters.UserId = profile.Data.Id;
            state.Parameters.UserName = profile.Data.Username;
            if (!IsCurrentState(state))
            {
                return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
            }

            return await CommitSessionAsync(state, linked.Token);
        }
        catch (OperationCanceledException) when (IsCancellationRequested(state, cancellationToken))
        {
            throw;
        }
        catch (Exception exception)
        {
            return AuthenticationOperationResult.Failure(ErrorKeyForException(exception));
        }
    }

    private async Task<AuthenticationOperationResult> CommitSessionAsync(
        AuthenticationAttemptState state,
        CancellationToken cancellationToken)
    {
        await _sessionCommitGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrentState(state))
            {
                return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
            }

            var previousSession = await _secureSessionStore.LoadAsync(cancellationToken);
            var nextSession = new StoredSession(
                state.Parameters.AccessToken.Value,
                state.Parameters.RefreshToken.Value);

            try
            {
                await _secureSessionStore.SaveAsync(nextSession, cancellationToken);
            }
            catch (Exception exception)
            {
                await RestoreStoredSessionAsync(previousSession);
                if (exception is OperationCanceledException && IsCancellationRequested(state, cancellationToken))
                {
                    throw;
                }

                return AuthenticationOperationResult.Failure("Error_LoginSessionSaveFailed");
            }

            bool applied;
            lock (_attemptSync)
            {
                if (!IsCurrentStateLocked(state))
                {
                    applied = false;
                }
                else
                {
                    var sharedParameters = state.Connection.ConnectionParameters;
                    var candidate = SnapshotConnectionParameters(sharedParameters);
                    candidate.AccessToken = CopyToken(state.Parameters.AccessToken);
                    candidate.RefreshToken = CopyToken(state.Parameters.RefreshToken);
                    candidate.UserId = state.Parameters.UserId;
                    candidate.UserName = state.Parameters.UserName;

                    var initialized = _webApi.CreateAC(
                        candidate,
                        Environment.MachineName,
                        ClientMetadata.OperatingSystem,
                        ClientMetadata.AppName,
                        ClientMetadata.AppVersion,
                        string.Empty);
                    if (!initialized.IsSuccess)
                    {
                        applied = false;
                    }
                    else
                    {
                        sharedParameters.AccessToken = CopyToken(candidate.AccessToken);
                        sharedParameters.RefreshToken = CopyToken(candidate.RefreshToken);
                        sharedParameters.UserId = candidate.UserId;
                        sharedParameters.UserName = candidate.UserName;
                        applied = true;
                        state.IsCompleted = true;
                        _activeAttempt = null;
                        _attemptGeneration++;
                    }
                }
            }

            if (!applied)
            {
                await RestoreStoredSessionAsync(previousSession);
                return AuthenticationOperationResult.Failure("Error_LoginUnavailable");
            }

            return AuthenticationOperationResult.Success();
        }
        finally
        {
            _sessionCommitGate.Release();
        }
    }

    private void FinishSuccessfulAttempt(AuthenticationAttemptState state)
    {
        lock (_attemptSync)
        {
            if (_activeAttempt == state)
            {
                _activeAttempt = null;
                _attemptGeneration++;
            }

            state.IsCompleted = true;
        }
    }

    private async Task RestoreStoredSessionAsync(StoredSession? previousSession)
    {
        try
        {
            if (previousSession is null)
            {
                await _secureSessionStore.ClearAsync(CancellationToken.None);
            }
            else
            {
                await _secureSessionStore.SaveAsync(previousSession, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // The login remains failed; the caller never treats a partial write as a session.
        }
    }

    private bool TryGetCurrentAttempt(AuthenticationAttempt attempt, out AuthenticationAttemptState state)
    {
        lock (_attemptSync)
        {
            if (_activeAttempt is { } current && current.Handle == attempt && IsCurrentStateLocked(current))
            {
                state = current;
                return true;
            }
        }

        state = null!;
        return false;
    }

    private bool IsCurrentState(AuthenticationAttemptState state)
    {
        lock (_attemptSync)
        {
            return IsCurrentStateLocked(state);
        }
    }

    private bool IsCurrentStateLocked(AuthenticationAttemptState state) =>
        _activeAttempt == state &&
        state.Generation == _attemptGeneration &&
        !state.IsCompleted &&
        ReferenceEquals(_session.CurrentConnection, state.Connection);

    private async Task RetireAttemptAsync(AuthenticationAttemptState state)
    {
        var retirement = state.BeginRetirement(() => RetireAttemptCoreAsync(state));
        await retirement;
    }

    private async Task RetireAttemptCoreAsync(AuthenticationAttemptState state)
    {
        using var timeout = new CancellationTokenSource(AttemptRetirementTimeout);
        if (!state.IsCompleted)
        {
            state.Cancel();
            var reference = state.GetChallenge();
            if (reference is not null)
            {
                try
                {
                    await state.WebApi.CancelAuthChallenge(reference, timeout.Token);
                }
                catch (Exception)
                {
                }
            }
        }

        try
        {
            await state.WaitForOperationsAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
        }

        state.DisposeTransport();
    }

    private async Task TryCancelChallengeOnRetiredAttemptAsync(
        AuthenticationAttemptState state,
        BarkFluff.Proto.Identity.AuthChallengeReference reference)
    {
        using var timeout = new CancellationTokenSource(AttemptRetirementTimeout);
        try
        {
            await state.WebApi.CancelAuthChallenge(CopyReference(reference), timeout.Token);
            state.ClearChallenge();
        }
        catch (Exception)
        {
        }
    }

    private static SignInChallengeResult MapChallenge(BarkFluff.Proto.Identity.AuthChallengeResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.ErrorCode))
        {
            return SignInChallengeResult.Failure(response.ErrorCode, ErrorKeyForCode(response.ErrorCode));
        }

        if (string.IsNullOrWhiteSpace(response.Challenge?.Id) || string.IsNullOrWhiteSpace(response.Challenge.Secret))
        {
            return SignInChallengeResult.Failure(null, "Error_LoginUnavailable");
        }

        DateTimeOffset? expiresAt = response.ExpiresAt is { Seconds: > 0 } timestamp
            ? timestamp.ToDateTimeOffset()
            : null;
        return SignInChallengeResult.Success(new SignInChallenge(
            CopyReference(response.Challenge),
            response.State,
            response.Factor,
            response.AvailableFactors.ToArray(),
            response.NeedsCode,
            response.TelegramUrl,
            expiresAt,
            null));
    }

    private static string ErrorKeyForCode(string errorCode) => errorCode switch
    {
        "account_not_found" => "Error_LoginAccountNotFound",
        "login_mode_disabled" => "Error_LoginModeChanged",
        "invalid_code" => "Error_LoginInvalidCode",
        "rate_limited" => "Error_LoginRateLimited",
        "locked" => "Error_LoginLocked",
        "protection_unavailable" => "Error_LoginProtectionUnavailable",
        _ => "Error_LoginFailed"
    };

    private static string ErrorKeyForException(Exception exception) => exception switch
    {
        BarkFluff.Shared.Exceptions.Identity.IdentityRateLimitExceededException => "Error_LoginRateLimited",
        BarkFluff.Shared.Exceptions.Identity.IdentityLockoutException => "Error_LoginLocked",
        BarkFluff.Shared.Exceptions.Identity.IdentityProtectionUnavailableException => "Error_LoginProtectionUnavailable",
        RpcException { StatusCode: StatusCode.Unimplemented } => "Error_LoginUpdateRequired",
        _ => "Error_LoginUnavailable"
    };

    private static GlobalParam SnapshotConnectionParameters(GlobalParam source)
    {
        var snapshot = JsonSerializer.Deserialize<GlobalParam>(JsonSerializer.Serialize(source))
            ?? throw new InvalidOperationException("Could not snapshot node connection parameters.");
        snapshot.AccessToken = null!;
        snapshot.RefreshToken = null!;
        return snapshot;
    }

    private static BarkFluff.Proto.Identity.AuthChallengeReference CopyReference(
        BarkFluff.Proto.Identity.AuthChallengeReference reference) => new()
    {
        Id = reference.Id,
        Secret = reference.Secret
    };

    private static BarkFluff.Proto.Identity.Token CopyToken(BarkFluff.Proto.Identity.Token token) => new()
    {
        Value = token.Value,
        ExpirationDate = token.ExpirationDate
    };

    private static CancellationTokenSource LinkCancellation(
        AuthenticationAttemptState state,
        CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(state.CancellationToken, cancellationToken);

    private static bool IsCancellationRequested(AuthenticationAttemptState state, CancellationToken cancellationToken) =>
        state.CancellationToken.IsCancellationRequested || cancellationToken.IsCancellationRequested;

    private sealed class AuthenticationAttemptState(
        AuthenticationAttempt handle,
        NodeConnection connection,
        GlobalParam parameters,
        WebApiClient webApi,
        long generation)
    {
        private readonly object _sync = new();
        private TaskCompletionSource _operationsIdle = CompletedSource();
        private Task? _retirementTask;
        private int _operationCount;
        private BarkFluff.Proto.Identity.AuthChallengeReference? _challenge;
        private BarkFluff.Proto.Identity.Token? _pendingAccessToken;
        private BarkFluff.Proto.Identity.Token? _pendingRefreshToken;
        private bool _disposed;

        public AuthenticationAttempt Handle { get; } = handle;
        public NodeConnection Connection { get; } = connection;
        public GlobalParam Parameters { get; } = parameters;
        public WebApiClient WebApi { get; } = webApi;
        public long Generation { get; } = generation;
        public CancellationTokenSource Lifetime { get; } = new();
        public CancellationToken CancellationToken => Lifetime.Token;
        public bool AuthorizedClientInitialized { get; set; }
        public bool IsCompleted { get; set; }
        public BarkFluff.Proto.Identity.Token? PendingAccessToken => _pendingAccessToken;
        public BarkFluff.Proto.Identity.Token? PendingRefreshToken => _pendingRefreshToken;

        public IDisposable EnterOperation()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_operationCount++ == 0)
                {
                    _operationsIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            return new OperationLease(this);
        }

        public Task WaitForOperationsAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return _operationsIdle.Task.WaitAsync(cancellationToken);
            }
        }

        public void SetChallenge(BarkFluff.Proto.Identity.AuthChallengeReference reference)
        {
            lock (_sync)
            {
                _challenge = CopyReference(reference);
            }
        }

        public BarkFluff.Proto.Identity.AuthChallengeReference? GetChallenge()
        {
            lock (_sync)
            {
                return _challenge is null ? null : CopyReference(_challenge);
            }
        }

        public void ClearChallenge()
        {
            lock (_sync)
            {
                _challenge = null;
            }
        }

        public void SetPendingTokens(
            BarkFluff.Proto.Identity.Token accessToken,
            BarkFluff.Proto.Identity.Token refreshToken)
        {
            _pendingAccessToken = CopyToken(accessToken);
            _pendingRefreshToken = CopyToken(refreshToken);
        }

        public Task BeginRetirement(Func<Task> retire)
        {
            lock (_sync)
            {
                return _retirementTask ??= retire();
            }
        }

        public void Cancel() => Lifetime.Cancel();

        public void DisposeTransport()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            WebApi.Dispose();
            Lifetime.Dispose();
        }

        private void ExitOperation()
        {
            lock (_sync)
            {
                if (--_operationCount == 0)
                {
                    _operationsIdle.TrySetResult();
                }
            }
        }

        private static TaskCompletionSource CompletedSource()
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult();
            return source;
        }

        private sealed class OperationLease(AuthenticationAttemptState owner) : IDisposable
        {
            private AuthenticationAttemptState? _owner = owner;

            public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ExitOperation();
        }
    }
}
