using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Text;

using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.WebApi.Core.MessengerData;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class AuthenticationServiceSignInTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupDuringPollingOrQrSave_InvalidatesBeforeWaitingAndNeverNavigates(bool qr)
    {
        await using var node = await TestIdentityNode.StartAsync("cleanup",
            resolveLoginMode: AuthLoginMode.TelegramLogin);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var connection = CreateConnection(node.Address, "device");
        var previous = new StoredSession("previous-access", "previous-refresh");
        var store = new TestSessionStore { Value = previous, BlockFirstSave = true };
        var authentication = new AuthenticationService(mainWebApi, new TestClientSession(connection), store);
        var navigation = new TestLoginNavigation();
        var viewModel = new LoginViewModel(authentication, navigation, new TestLoginLocalization()) { LoginOrEmail = "alice" };

        var login = qr ? viewModel.LoadFastAuthAsync() : viewModel.LoginCommand.ExecuteAsync(null);
        await store.FirstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cleanup = viewModel.CleanupAsync();
        if (!qr)
        {
            // If invalidation waits for the polling task, this event can never arrive.
            await node.CancelledChallengeSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        store.ReleaseFirstSave();
        await Task.WhenAll(login, cleanup).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(previous, store.Value);
        Assert.Null(connection.ConnectionParameters.AccessToken);
        Assert.Equal(0, navigation.ShowMessengerCalls);
        Assert.Null(viewModel.FastAuthQrCode);
    }

    [Fact]
    public async Task CleanupAfterCommit_PreservesCommittedSessionAndNeverNavigatesAgain()
    {
        await using var node = await TestIdentityNode.StartAsync("committed", approveBegin: true);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var connection = CreateConnection(node.Address, "device");
        var store = new TestSessionStore();
        var authentication = new AuthenticationService(mainWebApi, new TestClientSession(connection), store);
        var navigation = new TestLoginNavigation();
        var viewModel = new LoginViewModel(authentication, navigation, new TestLoginLocalization()) { LoginOrEmail = "alice" };
        await viewModel.LoginCommand.ExecuteAsync(null);
        viewModel.Password = "password";
        await viewModel.LoginCommand.ExecuteAsync(null);
        await viewModel.CleanupAsync();

        Assert.Equal(new StoredSession("committed-access", "committed-refresh"), store.Value);
        Assert.Equal("committed-access", connection.ConnectionParameters.AccessToken.Value);
        Assert.Equal(1, navigation.ShowMessengerCalls);
        Assert.Equal(0, store.ClearCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeginGrpcError_DistinguishesWrongPasswordFromUnavailableNode(bool unavailable)
    {
        await using var node = await TestIdentityNode.StartAsync("begin-error",
            invalidPassword: !unavailable, beginUnavailable: unavailable);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var authentication = new AuthenticationService(mainWebApi,
            new TestClientSession(CreateConnection(node.Address, "device")), new TestSessionStore());
        var viewModel = new LoginViewModel(authentication, new TestLoginNavigation(), new TestLoginLocalization())
        {
            LoginOrEmail = "alice"
        };
        await viewModel.LoginCommand.ExecuteAsync(null);
        viewModel.Password = "wrong";
        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(unavailable ? "Error_LoginUnavailable" : "Error_LoginFailed", viewModel.ErrorMessage);
        Assert.Equal(LoginStep.Password, viewModel.Step);
        Assert.Equal(unavailable, viewModel.CanRetry);
        Assert.True(viewModel.LoginCommand.CanExecute(null));
        Assert.Single(node.BeginRequests);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task FlowCancelledDuringSaveThatIgnoresCancellation_RollsBackWithoutApplying()
    {
        await using var node = await TestIdentityNode.StartAsync("cancel-save");
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var connection = CreateConnection(node.Address, "device");
        var previous = new StoredSession("previous-access", "previous-refresh");
        var store = new TestSessionStore { Value = previous, BlockFirstSave = true };
        var authentication = new AuthenticationService(mainWebApi, new TestClientSession(connection), store);
        var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        var reference = await BeginChallengeAsync(authentication, attempt);
        using var cancellation = new CancellationTokenSource();

        var completion = authentication.CompleteAuthChallengeAsync(attempt, reference, "", false, cancellation.Token);
        await store.FirstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        store.ReleaseFirstSave();
        var result = await completion.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(result.IsSuccess);
        Assert.Equal(previous, store.Value);
        Assert.Null(connection.ConnectionParameters.AccessToken);
        await authentication.CancelAuthenticationAttemptAsync(attempt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfileRetry_ReusesTokensOnlyForExactChallengeReference(bool changeSecret)
    {
        await using var node = await TestIdentityNode.StartAsync("pending", failFirstProfile: true);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var store = new TestSessionStore();
        var authentication = new AuthenticationService(mainWebApi,
            new TestClientSession(CreateConnection(node.Address, "device")), store);
        var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        var first = await BeginChallengeAsync(authentication, attempt);
        Assert.False((await authentication.CompleteAuthChallengeAsync(attempt, first, "111111", false)).IsSuccess);

        var second = await BeginChallengeAsync(authentication, attempt);
        second = new AuthChallengeReference
        {
            Id = changeSecret ? first.Id : "second-challenge",
            Secret = changeSecret ? "different-secret" : first.Secret
        };
        Assert.True((await authentication.CompleteAuthChallengeAsync(attempt, second, "222222", false)).IsSuccess);
        Assert.Equal(new[] { first, second }, node.CompletedReferences.ToArray());
        Assert.Equal(new StoredSession("pending-access", "pending-refresh"), store.Value);
    }

    [Fact]
    public async Task ProfileRetry_SameReferenceDoesNotVerifyConsumedCodeAgain()
    {
        await using var node = await TestIdentityNode.StartAsync("retry", failFirstProfile: true);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var authentication = new AuthenticationService(mainWebApi,
            new TestClientSession(CreateConnection(node.Address, "device")), new TestSessionStore());
        var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        var reference = await BeginChallengeAsync(authentication, attempt);
        Assert.False((await authentication.CompleteAuthChallengeAsync(attempt, reference, "111111", false)).IsSuccess);
        Assert.True((await authentication.CompleteAuthChallengeAsync(attempt, reference.Clone(), "111111", false)).IsSuccess);
        Assert.Single(node.CompletedReferences);
    }

    [Fact]
    public async Task AttemptSwitch_CancelsLateChallengeOnOldNodeAndKeepsMetadata()
    {
        await using var oldNode = await TestIdentityNode.StartAsync("old");
        await using var newNode = await TestIdentityNode.StartAsync("new");
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var session = new TestClientSession(CreateConnection(oldNode.Address, "device-old"));
        var store = new TestSessionStore();
        var authentication = new AuthenticationService(mainWebApi, session, store);

        var oldAttempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        var resolution = await authentication.ResolveSignInAsync(oldAttempt, "alice@example.com");
        Assert.True(resolution.IsSuccess);
        var oldChallenge = await authentication.BeginSignInAsync(
            oldAttempt,
            "alice@example.com",
            "password",
            AuthLoginMode.Password,
            OtpTypeId.Unknown,
            false);
        Assert.True(oldChallenge.IsSuccess);
        Assert.Equal("old-challenge", oldChallenge.Challenge!.Reference.Id);
        Assert.True(oldNode.BeginRequests.TryPeek(out var beginRequest));
        Assert.True(beginRequest.ReportLoginModeMismatch);
        Assert.True(oldNode.RequestMetadata.TryPeek(out var headers));
        Assert.Equal(Environment.MachineName, DecodeHeader(headers, "x-device-name"));
        Assert.Equal("device-old", DecodeHeader(headers, "x-device-id"));
        Assert.False(string.IsNullOrWhiteSpace(DecodeHeader(headers, "x-os-name")));
        Assert.False(string.IsNullOrWhiteSpace(DecodeHeader(headers, "x-app-name")));

        session.SetConnection(CreateConnection(newNode.Address, "device-new"));
        var newAttempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        await oldNode.CancelledChallengeSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("old-challenge", Assert.Single(oldNode.CancelledReferences).Id);
        Assert.Empty(newNode.CancelledReferences);
        var newResolution = await authentication.ResolveSignInAsync(newAttempt, "bob");
        Assert.True(newResolution.IsSuccess);
        Assert.Equal("bob", Assert.Single(newNode.ResolvedLogins));

        await authentication.CancelAuthenticationAttemptAsync(newAttempt);
    }

    [Fact]
    public async Task ResolveSignIn_LeavesSavedAndAuthorizedSessionUntouched()
    {
        await using var node = await TestIdentityNode.StartAsync("active-session");
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var connection = CreateConnection(node.Address, "device-active");
        connection.ConnectionParameters.AccessToken = new Token { Value = "active-access" };
        connection.ConnectionParameters.RefreshToken = new Token { Value = "active-refresh" };
        connection.ConnectionParameters.UserId = 42;
        connection.ConnectionParameters.UserName = "existing-user";
        Assert.True(mainWebApi.CreateAC(
            connection.ConnectionParameters,
            Environment.MachineName,
            ClientMetadata.OperatingSystem,
            ClientMetadata.AppName,
            ClientMetadata.AppVersion,
            string.Empty).IsSuccess);

        var session = new TestClientSession(connection);
        var store = new TestSessionStore { Value = new StoredSession("saved-access", "saved-refresh") };
        var authentication = new AuthenticationService(mainWebApi, session, store);
        var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());

        var result = await authentication.ResolveSignInAsync(attempt, "alice");

        Assert.True(result.IsSuccess);
        Assert.Equal("active-access", connection.ConnectionParameters.AccessToken.Value);
        Assert.Equal("active-refresh", connection.ConnectionParameters.RefreshToken.Value);
        Assert.Equal(42, connection.ConnectionParameters.UserId);
        Assert.Equal("existing-user", connection.ConnectionParameters.UserName);
        Assert.Equal(new StoredSession("saved-access", "saved-refresh"), store.Value);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, store.ClearCount);
        Assert.Empty(node.AuthRequests);
        Assert.Empty(node.BeginRequests);

        var activeProfile = await mainWebApi.GetUserData(connection.ConnectionParameters);
        Assert.True(activeProfile.Error.IsSuccess);
        Assert.Equal("active-access", Assert.Single(node.UserRequestMetadata)["x-auth-token"]);
        await authentication.CancelAuthenticationAttemptAsync(attempt);
    }

    [Fact]
    public async Task ResolveSignIn_MapsIdentityGrpcExceptionsFromTransportTrailers()
    {
        var cases = new[]
        {
            (new IdentityRateLimitExceededException().ErrorCode, "8", "Error_LoginRateLimited"),
            (new IdentityLockoutException().ErrorCode, "8", "Error_LoginLocked"),
            (new IdentityProtectionUnavailableException().ErrorCode, "14", "Error_LoginProtectionUnavailable")
        };

        foreach (var (errorCode, grpcStatus, expectedResourceKey) in cases)
        {
            await using var node = await TestIdentityNode.StartAsync(
                "resolve-error",
                resolveGrpcErrorCode: errorCode,
                resolveGrpcStatus: grpcStatus);
            using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
            var authentication = new AuthenticationService(
                mainWebApi,
                new TestClientSession(CreateConnection(node.Address, "device")),
                new TestSessionStore());
            var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());

            var result = await authentication.ResolveSignInAsync(attempt, "alice");

            Assert.False(result.IsSuccess);
            Assert.Equal(expectedResourceKey, result.ErrorResourceKey);
            Assert.Single(node.ResolvedLogins);
            Assert.Empty(node.BeginRequests);
            await authentication.CancelAuthenticationAttemptAsync(attempt);
        }
    }

    [Fact]
    public async Task BeginSignIn_MapsLoginModeMismatchFromResponse()
    {
        await using var node = await TestIdentityNode.StartAsync("mode-mismatch", beginErrorCode: "login_mode_disabled");
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var authentication = new AuthenticationService(
            mainWebApi,
            new TestClientSession(CreateConnection(node.Address, "device")),
            new TestSessionStore());
        var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        var resolution = await authentication.ResolveSignInAsync(attempt, "alice");

        var result = await authentication.BeginSignInAsync(
            attempt,
            "alice",
            "password",
            resolution.LoginMode,
            OtpTypeId.Unknown,
            false);

        Assert.False(result.IsSuccess);
        Assert.Equal("login_mode_disabled", result.ErrorCode);
        Assert.Equal("Error_LoginModeChanged", result.ErrorResourceKey);
        Assert.Single(node.BeginRequests);
        await authentication.CancelAuthenticationAttemptAsync(attempt);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CompleteMissingTokenResponse_DoesNotApplyPersistOrNavigate(
        bool omitAccessToken,
        bool omitRefreshToken)
    {
        await using var node = await TestIdentityNode.StartAsync(
            "missing-token",
            approveBegin: true,
            omitAccessToken: omitAccessToken,
            omitRefreshToken: omitRefreshToken);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var connection = CreateConnection(node.Address, "device");
        connection.ConnectionParameters.AccessToken = new Token { Value = "previous-access" };
        connection.ConnectionParameters.RefreshToken = new Token { Value = "previous-refresh" };
        var store = new TestSessionStore { Value = new StoredSession("saved-access", "saved-refresh") };
        var authentication = new AuthenticationService(mainWebApi, new TestClientSession(connection), store);
        var navigation = new TestLoginNavigation();
        var viewModel = new LoginViewModel(authentication, navigation, new TestLoginLocalization())
        {
            LoginOrEmail = "alice"
        };
        await viewModel.LoginCommand.ExecuteAsync(null);
        viewModel.Password = "password";
        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(LoginStep.Challenge, viewModel.Step);
        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal("Error_LoginMissingTokens", viewModel.ErrorMessage);
        Assert.Equal("previous-access", connection.ConnectionParameters.AccessToken.Value);
        Assert.Equal("previous-refresh", connection.ConnectionParameters.RefreshToken.Value);
        Assert.Equal(new StoredSession("saved-access", "saved-refresh"), store.Value);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, store.ClearCount);
        Assert.Equal(0, navigation.ShowMessengerCalls);
        Assert.Empty(node.UserRequestMetadata);
        Assert.Single(node.CompletedReferences);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task LoginViewModel_UnknownNumericLoginModeDoesNotShowPasswordOrBeginSignIn()
    {
        await using var node = await TestIdentityNode.StartAsync(
            "unknown-mode",
            resolveLoginMode: (AuthLoginMode)999);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var authentication = new AuthenticationService(
            mainWebApi,
            new TestClientSession(CreateConnection(node.Address, "device")),
            new TestSessionStore());
        var viewModel = new LoginViewModel(authentication, new TestLoginNavigation(), new TestLoginLocalization())
        {
            LoginOrEmail = "alice"
        };

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(LoginStep.Identifier, viewModel.Step);
        Assert.Equal("Error_LoginAccountNotFound", viewModel.ErrorMessage);
        Assert.Empty(viewModel.Password);
        Assert.Empty(node.AuthRequests);
        Assert.Empty(node.BeginRequests);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task ConcurrentLoginCommandAttempts_DoNotDuplicateBeginCompletePersistenceOrNavigation()
    {
        await using var node = await TestIdentityNode.StartAsync(
            "concurrent-login",
            blockBegin: true,
            blockComplete: true,
            approveBegin: true);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var connection = CreateConnection(node.Address, "device");
        var store = new TestSessionStore();
        var navigation = new TestLoginNavigation();
        var authentication = new AuthenticationService(mainWebApi, new TestClientSession(connection), store);
        var viewModel = new LoginViewModel(authentication, navigation, new TestLoginLocalization())
        {
            LoginOrEmail = "alice"
        };
        await viewModel.LoginCommand.ExecuteAsync(null);
        Assert.Equal(LoginStep.Password, viewModel.Step);
        viewModel.Password = "password";

        var firstLogin = viewModel.LoginCommand.ExecuteAsync(null);
        await node.BeginRequestSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var repeatedBegin = viewModel.LoginCommand.ExecuteAsync(null);
        Assert.Single(node.BeginRequests);

        node.ReleaseBegin();
        await node.CompleteRequestSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var repeatedComplete = viewModel.LoginCommand.ExecuteAsync(null);
        Assert.Single(node.CompletedReferences);
        node.ReleaseComplete();

        await Task.WhenAll(firstLogin, repeatedBegin, repeatedComplete).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Single(node.BeginRequests);
        Assert.Single(node.CompletedReferences);
        Assert.Empty(node.AuthRequests);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(new StoredSession("concurrent-login-access", "concurrent-login-refresh"), store.Value);
        Assert.Equal(1, navigation.ShowMessengerCalls);
        Assert.Equal("concurrent-login-access", connection.ConnectionParameters.AccessToken.Value);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task ReplacingAttemptDuringProfileLoad_CancelsProfileCallAndNeverPersistsTokens()
    {
        await using var node = await TestIdentityNode.StartAsync("profile", blockProfile: true);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var connection = CreateConnection(node.Address, "device-profile");
        var session = new TestClientSession(connection);
        var store = new TestSessionStore { Value = new StoredSession("previous-access", "previous-refresh") };
        var authentication = new AuthenticationService(mainWebApi, session, store);
        var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        var reference = await BeginChallengeAsync(authentication, attempt);

        var completion = authentication.CompleteAuthChallengeAsync(attempt, reference, string.Empty, false);
        await node.ProfileRequestSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var replacement = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        node.ReleaseProfile();

        var result = await completion;
        Assert.False(result.IsSuccess);
        Assert.Equal("Error_LoginUnavailable", result.ErrorResourceKey);
        Assert.Equal(new StoredSession("previous-access", "previous-refresh"), store.Value);
        Assert.Null(connection.ConnectionParameters.AccessToken);
        Assert.Null(connection.ConnectionParameters.RefreshToken);
        await authentication.CancelAuthenticationAttemptAsync(replacement);
    }

    [Fact]
    public async Task AttemptReplacedDuringStartedSave_RestoresPreviousSnapshotBeforeNewAttemptCommits()
    {
        await using var oldNode = await TestIdentityNode.StartAsync("old-save");
        await using var newNode = await TestIdentityNode.StartAsync("new-save");
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var oldConnection = CreateConnection(oldNode.Address, "device-old");
        var newConnection = CreateConnection(newNode.Address, "device-new");
        var session = new TestClientSession(oldConnection);
        var store = new TestSessionStore
        {
            Value = new StoredSession("previous-access", "previous-refresh"),
            BlockFirstSave = true
        };
        var authentication = new AuthenticationService(mainWebApi, session, store);
        var oldAttempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        var oldReference = await BeginChallengeAsync(authentication, oldAttempt);

        var oldCompletion = authentication.CompleteAuthChallengeAsync(oldAttempt, oldReference, string.Empty, false);
        var firstSaveOrCompletion = await Task.WhenAny(store.FirstSaveStarted.Task, oldCompletion);
        if (firstSaveOrCompletion == oldCompletion)
        {
            var earlyResult = await oldCompletion;
            Assert.Fail($"Authentication ended before persistence started: {earlyResult.ErrorResourceKey}");
        }

        await store.FirstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        session.SetConnection(newConnection);
        var newAttempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());
        store.ReleaseFirstSave();
        var staleResult = await oldCompletion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(staleResult.IsSuccess);
        Assert.Equal("Error_LoginUnavailable", staleResult.ErrorResourceKey);

        var newReference = await BeginChallengeAsync(authentication, newAttempt);
        var newResult = await authentication.CompleteAuthChallengeAsync(newAttempt, newReference, string.Empty, false);

        Assert.True(newResult.IsSuccess);
        Assert.Equal(new StoredSession("new-save-access", "new-save-refresh"), store.Value);
        Assert.Equal("new-save-access", newConnection.ConnectionParameters.AccessToken?.Value);
        Assert.Null(oldConnection.ConnectionParameters.AccessToken);
        await oldNode.CancelledChallengeSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("old-save-challenge", Assert.Single(oldNode.CancelledReferences).Id);
    }

    [Fact]
    public async Task ResolveUnimplemented_ReportsServerUpdateRequiredWithoutLegacyFallback()
    {
        await using var node = await TestIdentityNode.StartAsync("old-api", unimplementedResolve: true);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var session = new TestClientSession(CreateConnection(node.Address, "device"));
        var authentication = new AuthenticationService(mainWebApi, session, new TestSessionStore());
        var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());

        var result = await authentication.ResolveSignInAsync(attempt, "alice");

        Assert.False(result.IsSuccess);
        Assert.Equal("Error_LoginUpdateRequired", result.ErrorResourceKey);
        Assert.Empty(node.ResolvedLogins);
        await authentication.CancelAuthenticationAttemptAsync(attempt);
    }

    [Fact]
    public async Task ResolveUnspecifiedMode_IsTreatedAsUnknownAccount()
    {
        await using var node = await TestIdentityNode.StartAsync(
            "unspecified-mode",
            resolveLoginMode: AuthLoginMode.LoginModeUnspecified);
        using var mainWebApi = new BarkFluff.WebApi.Core.WebApi();
        var session = new TestClientSession(CreateConnection(node.Address, "device"));
        var authentication = new AuthenticationService(mainWebApi, session, new TestSessionStore());
        var attempt = Assert.IsType<AuthenticationAttempt>(authentication.CreateAuthenticationAttempt());

        var result = await authentication.ResolveSignInAsync(attempt, "alice");

        Assert.False(result.IsSuccess);
        Assert.Equal("account_not_found", result.ErrorCode);
        Assert.Equal("Error_LoginAccountNotFound", result.ErrorResourceKey);
        await authentication.CancelAuthenticationAttemptAsync(attempt);
    }

    private static async Task<AuthChallengeReference> BeginChallengeAsync(
        AuthenticationService authentication,
        AuthenticationAttempt attempt)
    {
        var resolution = await authentication.ResolveSignInAsync(attempt, "alice");
        Assert.True(resolution.IsSuccess);
        var begin = await authentication.BeginSignInAsync(
            attempt,
            "alice",
            "password",
            resolution.LoginMode,
            OtpTypeId.Unknown,
            false);
        Assert.True(begin.IsSuccess);
        return begin.Challenge!.Reference;
    }

    private static NodeConnection CreateConnection(string address, string deviceId)
    {
        var parameters = new GlobalParam
        {
            SocketBeacon = address,
            SocketUsers = address,
            SocketIdentity = address,
            SocketFiles = address,
            SocketMessages = address,
            SocketUpdates = address,
            SocketOnliner = address,
            SocketFastAuth = address,
            DeviceId = deviceId
        };
        return new NodeConnection(new NodeProfile(address, "Test node", "Local gRPC fixture"), parameters);
    }

    private static string DecodeHeader(IReadOnlyDictionary<string, string> headers, string name) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(headers[name]));

    private sealed class TestClientSession(NodeConnection connection) : IClientSession
    {
        public NodeConnection? CurrentConnection { get; private set; } = connection;
        public void SetConnection(NodeConnection value) => CurrentConnection = value;
    }

    private sealed class TestSessionStore : ISecureSessionStore
    {
        private int _firstSaveClaimed;
        private int _saveCount;
        private int _clearCount;
        private readonly TaskCompletionSource _releaseFirstSave = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StoredSession? Value { get; set; }
        public bool BlockFirstSave { get; init; }
        public int SaveCount => Volatile.Read(ref _saveCount);
        public int ClearCount => Volatile.Read(ref _clearCount);
        public TaskCompletionSource FirstSaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);

        public async Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _saveCount);
            if (BlockFirstSave && Interlocked.Exchange(ref _firstSaveClaimed, 1) == 0)
            {
                FirstSaveStarted.TrySetResult();
                await _releaseFirstSave.Task;
            }

            Value = session;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _clearCount);
            Value = null;
            return Task.CompletedTask;
        }

        public void ReleaseFirstSave() => _releaseFirstSave.TrySetResult();
    }

    private sealed class TestIdentityNode : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private readonly TaskCompletionSource _releaseProfile = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseBegin = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TestIdentityNode(WebApplication application, string address, string name)
        {
            _application = application;
            Address = address;
            Name = name;
        }

        public string Address { get; }
        public string Name { get; }
        public bool BlockProfile { get; init; }
        public bool UnimplementedResolve { get; init; }
        public AuthLoginMode ResolveLoginMode { get; init; } = AuthLoginMode.Password;
        public string? ResolveGrpcErrorCode { get; init; }
        public string ResolveGrpcStatus { get; init; } = "0";
        public string? BeginErrorCode { get; init; }
        public bool BlockBegin { get; init; }
        public bool BlockComplete { get; init; }
        public bool ApproveBegin { get; init; }
        public bool OmitAccessToken { get; init; }
        public bool OmitRefreshToken { get; init; }
        public bool FailFirstProfile { get; init; }
        public bool InvalidPassword { get; init; }
        public bool BeginUnavailable { get; init; }
        public ConcurrentQueue<string> ResolvedLogins { get; } = new();
        public ConcurrentQueue<AuthRequest> AuthRequests { get; } = new();
        public ConcurrentQueue<BeginSignInRequest> BeginRequests { get; } = new();
        public ConcurrentQueue<AuthChallengeReference> CompletedReferences { get; } = new();
        public ConcurrentQueue<AuthChallengeReference> CancelledReferences { get; } = new();
        public ConcurrentQueue<IReadOnlyDictionary<string, string>> RequestMetadata { get; } = new();
        public ConcurrentQueue<IReadOnlyDictionary<string, string>> UserRequestMetadata { get; } = new();
        public TaskCompletionSource CancelledChallengeSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProfileRequestSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BeginRequestSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CompleteRequestSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<TestIdentityNode> StartAsync(
            string name,
            bool blockProfile = false,
            bool unimplementedResolve = false,
            AuthLoginMode resolveLoginMode = AuthLoginMode.Password,
            string? resolveGrpcErrorCode = null,
            string resolveGrpcStatus = "0",
            string? beginErrorCode = null,
            bool blockBegin = false,
            bool blockComplete = false,
            bool approveBegin = false,
            bool omitAccessToken = false,
            bool omitRefreshToken = false,
            bool failFirstProfile = false,
            bool invalidPassword = false,
            bool beginUnavailable = false)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listenOptions =>
                listenOptions.Protocols = HttpProtocols.Http2));
            var application = builder.Build();
            TestIdentityNode? node = null;

            application.MapPost("/barkfluff.identity.IdentityApi/Auth", async context =>
            {
                var payload = await ReadGrpcPayloadAsync(context);
                node!.AuthRequests.Enqueue(AuthRequest.Parser.ParseFrom(payload));
                await WriteGrpcResponseAsync(context, new AuthResponse());
            });

            application.MapPost("/barkfluff.identity.IdentityApi/ResolveSignIn", async context =>
            {
                var payload = await ReadGrpcPayloadAsync(context);
                var request = ResolveSignInRequest.Parser.ParseFrom(payload);
                if (node!.UnimplementedResolve)
                {
                    await WriteGrpcResponseAsync(context, new ResolveSignInResponse(), "12");
                    return;
                }

                node.ResolvedLogins.Enqueue(request.Login);
                node.RequestMetadata.Enqueue(CaptureHeaders(context));
                if (node.ResolveGrpcErrorCode is not null)
                {
                    await WriteGrpcResponseAsync(
                        context,
                        new ResolveSignInResponse(),
                        node.ResolveGrpcStatus,
                        node.ResolveGrpcErrorCode);
                    return;
                }

                await WriteGrpcResponseAsync(context, new ResolveSignInResponse { LoginMode = node.ResolveLoginMode });
            });

            application.MapPost("/barkfluff.identity.IdentityApi/BeginSignIn", async context =>
            {
                var payload = await ReadGrpcPayloadAsync(context);
                var request = BeginSignInRequest.Parser.ParseFrom(payload);
                node!.BeginRequests.Enqueue(request);
                node.RequestMetadata.Enqueue(CaptureHeaders(context));
                node.BeginRequestSeen.TrySetResult();
                if (node.BeginUnavailable)
                {
                    await WriteGrpcResponseAsync(context, new AuthChallengeResponse(), "14");
                    return;
                }
                if (node.InvalidPassword)
                {
                    await WriteGrpcResponseAsync(context, new AuthChallengeResponse(), "16",
                        new InvalidLoginOrPasswordException().ErrorCode);
                    return;
                }
                if (node.BlockBegin)
                {
                    await node._releaseBegin.Task.WaitAsync(context.RequestAborted);
                }

                if (node.BeginErrorCode is not null)
                {
                    await WriteGrpcResponseAsync(context, new AuthChallengeResponse { ErrorCode = node.BeginErrorCode });
                    return;
                }

                await WriteGrpcResponseAsync(context, new AuthChallengeResponse
                {
                    Challenge = new AuthChallengeReference { Id = $"{node.Name}-challenge", Secret = "challenge-secret" },
                    State = node.ApproveBegin ? AuthChallengeState.Approved : AuthChallengeState.Waiting,
                    Factor = request.Factor,
                    NeedsCode = false
                });
            });

            application.MapPost("/barkfluff.identity.IdentityApi/CancelAuthChallenge", async context =>
            {
                var payload = await ReadGrpcPayloadAsync(context);
                var reference = AuthChallengeReference.Parser.ParseFrom(payload);
                node!.CancelledReferences.Enqueue(reference);
                node.CancelledChallengeSeen.TrySetResult();
                await WriteGrpcResponseAsync(context, new AuthChallengeResponse
                {
                    Challenge = reference,
                    State = AuthChallengeState.Cancelled
                });
            });

            application.MapPost("/barkfluff.identity.IdentityApi/CompleteAuthChallenge", async context =>
            {
                var payload = await ReadGrpcPayloadAsync(context);
                var request = CompleteAuthChallengeRequest.Parser.ParseFrom(payload);
                node!.CompletedReferences.Enqueue(request.Challenge);
                node.CompleteRequestSeen.TrySetResult();
                if (node.BlockComplete)
                {
                    await node._releaseComplete.Task.WaitAsync(context.RequestAborted);
                }

                var session = new AuthResponse();
                if (!node.OmitAccessToken)
                {
                    session.AccessToken = new Token { Value = $"{node.Name}-access" };
                }

                if (!node.OmitRefreshToken)
                {
                    session.RefreshToken = new Token { Value = $"{node.Name}-refresh" };
                }

                await WriteGrpcResponseAsync(context, new CompleteAuthChallengeResponse
                {
                    State = AuthChallengeState.Completed,
                    Session = session
                });
            });

            application.MapPost("/barkfluff.identity.IdentityApi/GetAuthChallenge", async context =>
            {
                var payload = await ReadGrpcPayloadAsync(context);
                await WriteGrpcResponseAsync(context, new AuthChallengeResponse
                {
                    Challenge = AuthChallengeReference.Parser.ParseFrom(payload),
                    State = AuthChallengeState.Approved,
                    Factor = OtpTypeId.Telegram
                });
            });

            application.MapPost("/barkfluff.fast.auth.FastAuthApi/GenerateFastAuthToken", async context =>
            {
                _ = await ReadGrpcPayloadAsync(context);
                await WriteGrpcResponseAsync(context, new GenerateFastAuthTokenResponse
                {
                    FastAuthId = "qr",
                    Token = new FastAuthToken { Value = "aGVsbG8=" },
                    ExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(5))
                });
            });

            application.MapPost("/barkfluff.fast.auth.FastAuthApi/SubscribeFastAuthResult", async context =>
            {
                _ = await ReadGrpcPayloadAsync(context);
                await WriteGrpcResponseAsync(context, new FastAuthResult
                {
                    Status = FastAuthStatus.Accepted,
                    AccessToken = $"{node!.Name}-qr-access",
                    RefreshToken = $"{node.Name}-qr-refresh",
                    AccessTokenExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(10)),
                    RefreshTokenExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(1))
                });
            });

            application.MapPost("/barkfluff.users.UsersApi/GetUser", async context =>
            {
                _ = await ReadGrpcPayloadAsync(context);
                node!.ProfileRequestSeen.TrySetResult();
                node.UserRequestMetadata.Enqueue(CaptureHeaders(context));
                if (node.FailFirstProfile && node.UserRequestMetadata.Count == 1)
                {
                    await WriteGrpcResponseAsync(context, new GetUserResponse(), "14");
                    return;
                }
                if (node.BlockProfile)
                {
                    try
                    {
                        await node._releaseProfile.Task.WaitAsync(context.RequestAborted);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                await WriteGrpcResponseAsync(context, new GetUserResponse
                {
                    User = new User
                    {
                        Id = 42,
                        Username = node.Name,
                        FirstName = "Test",
                        RegistrationDate = Timestamp.FromDateTime(DateTime.UtcNow)
                    }
                });
            });

            await application.StartAsync();
            node = new TestIdentityNode(application, application.Urls.Single(), name)
            {
                BlockProfile = blockProfile,
                UnimplementedResolve = unimplementedResolve,
                ResolveLoginMode = resolveLoginMode,
                ResolveGrpcErrorCode = resolveGrpcErrorCode,
                ResolveGrpcStatus = resolveGrpcStatus,
                BeginErrorCode = beginErrorCode,
                BlockBegin = blockBegin,
                BlockComplete = blockComplete,
                ApproveBegin = approveBegin,
                OmitAccessToken = omitAccessToken,
                OmitRefreshToken = omitRefreshToken,
                FailFirstProfile = failFirstProfile,
                InvalidPassword = invalidPassword,
                BeginUnavailable = beginUnavailable
            };
            return node;
        }

        public void ReleaseProfile() => _releaseProfile.TrySetResult();
        public void ReleaseBegin() => _releaseBegin.TrySetResult();
        public void ReleaseComplete() => _releaseComplete.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            _releaseProfile.TrySetResult();
            _releaseBegin.TrySetResult();
            _releaseComplete.TrySetResult();
            await _application.DisposeAsync();
        }

        private static IReadOnlyDictionary<string, string> CaptureHeaders(HttpContext context) =>
            context.Request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        private static async Task<byte[]> ReadGrpcPayloadAsync(HttpContext context)
        {
            var header = new byte[5];
            await ReadExactlyAsync(context.Request.Body, header, context.RequestAborted);
            var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
            var payload = new byte[length];
            await ReadExactlyAsync(context.Request.Body, payload, context.RequestAborted);
            return payload;
        }

        private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("Incomplete gRPC frame.");
                }

                offset += read;
            }
        }

        private static async Task WriteGrpcResponseAsync(
            HttpContext context,
            IMessage response,
            string grpcStatus = "0",
            string? errorCode = null)
        {
            var payload = response.ToByteArray();
            var frame = new byte[payload.Length + 5];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1, 4), payload.Length);
            payload.CopyTo(frame, 5);
            context.Response.ContentType = "application/grpc";
            context.Response.DeclareTrailer("grpc-status");
            if (errorCode is not null)
            {
                context.Response.DeclareTrailer("x-error-code");
            }

            await context.Response.Body.WriteAsync(frame, context.RequestAborted);
            context.Response.AppendTrailer("grpc-status", grpcStatus);
            if (errorCode is not null)
            {
                context.Response.AppendTrailer("x-error-code", errorCode);
            }
        }
    }

    private sealed class TestLoginNavigation : IOnboardingNavigationService
    {
        public int ShowMessengerCalls { get; private set; }
        public object? CurrentViewModel => null;
        public event EventHandler<OnboardingNavigationEventArgs>? CurrentViewModelChanged
        {
            add { }
            remove { }
        }

        public void ShowWelcome() { }
        public void ShowSelectNode() { }
        public void ShowConnectedNode() { }
        public void ShowLogin() { }
        public void ShowRegistration() { }
        public void ShowPasswordRecovery() { }
        public void ShowMessenger() => ShowMessengerCalls++;
    }

    private sealed class TestLoginLocalization : ILocalizationService
    {
        public string ResolveSupportedLanguage(string? requestedLanguage) => requestedLanguage ?? "en";
        public void Apply(string language) { }
        public string GetString(string resourceKey) => resourceKey;
    }
}
