using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Proto.Identity;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class LoginViewModelTests
{
    private const string ValidQrPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9JfWQAAAAASUVORK5CYII=";

    [Theory]
    [InlineData(" alice ", "alice")]
    [InlineData(" alice@example.com ", "alice@example.com")]
    public async Task ResolveIdentifier_TrimsAndNeverUsesLegacyLoginOrSendsCode(string entered, string expected)
    {
        var authentication = new FakeAuthenticationService();
        var viewModel = CreateViewModel(authentication);
        viewModel.LoginOrEmail = entered;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal([expected], authentication.ResolvedLogins);
        Assert.Empty(authentication.BeginCalls);
        Assert.Equal(0, authentication.LegacyLoginCalls);
        Assert.Equal(LoginStep.Password, viewModel.Step);
        Assert.Empty(viewModel.Password);
        Assert.Empty(viewModel.Code);
        await viewModel.CleanupAsync();
    }

    [Theory]
    [InlineData(AuthLoginMode.Password)]
    [InlineData(AuthLoginMode.PasswordSecondFactor)]
    [InlineData(AuthLoginMode.TelegramLogin)]
    public async Task LoginFlow_UsesResolvedModeAndTelegramStartsWithoutPassword(AuthLoginMode mode)
    {
        var authentication = new FakeAuthenticationService { LoginMode = mode };
        authentication.BeginResults.Enqueue(Challenge(
            AuthChallengeState.Waiting,
            mode == AuthLoginMode.TelegramLogin ? OtpTypeId.Telegram : OtpTypeId.Authenticator,
            needsCode: mode != AuthLoginMode.TelegramLogin,
            factors: [OtpTypeId.Authenticator, OtpTypeId.Email]));
        var viewModel = CreateViewModel(authentication);
        viewModel.LoginOrEmail = " alice ";

        await viewModel.LoginCommand.ExecuteAsync(null);
        if (mode == AuthLoginMode.TelegramLogin)
        {
            Assert.Single(authentication.BeginCalls);
            Assert.Empty(authentication.BeginCalls[0].Password);
            Assert.Equal(OtpTypeId.Telegram, authentication.BeginCalls[0].Factor);
            Assert.Equal(LoginStep.Challenge, viewModel.Step);
            Assert.False(viewModel.CanSelectFactor);
            Assert.Equal([OtpTypeId.Telegram], viewModel.AvailableFactors.Select(x => x.Factor));
        }
        else
        {
            Assert.Equal(LoginStep.Password, viewModel.Step);
            Assert.Empty(authentication.BeginCalls);
            viewModel.Password = "secret";
            await viewModel.LoginCommand.ExecuteAsync(null);
            Assert.Single(authentication.BeginCalls);
            Assert.Equal("secret", authentication.BeginCalls[0].Password);
            Assert.Equal(OtpTypeId.Unknown, authentication.BeginCalls[0].Factor);
            Assert.Equal(LoginStep.Challenge, viewModel.Step);
            Assert.Equal(mode == AuthLoginMode.PasswordSecondFactor, viewModel.CanUseRecoveryCode);
            Assert.Equal(mode == AuthLoginMode.PasswordSecondFactor, viewModel.CanSelectFactor);
        }

        Assert.Equal("alice", authentication.BeginCalls.Single().Login);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task PasswordSecondFactor_CanSwitchFactorAndRecoveryWithNewChallenges()
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.PasswordSecondFactor };
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Waiting, OtpTypeId.Authenticator, true,
            [OtpTypeId.Authenticator, OtpTypeId.Email]));
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Waiting, OtpTypeId.Email, true,
            [OtpTypeId.Authenticator, OtpTypeId.Email]));
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Waiting, OtpTypeId.Email, true,
            [OtpTypeId.Authenticator, OtpTypeId.Email]));
        var viewModel = await StartChallengeAsync(authentication, AuthLoginMode.PasswordSecondFactor);
        viewModel.Password = "secret";

        var factorRestarted = WhenAuthenticationStops(viewModel);
        viewModel.SelectedFactor = OtpTypeId.Email;
        await authentication.WaitForBeginCallsAsync(2);
        await factorRestarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(authentication.CancelledChallenges);
        Assert.Equal("first", authentication.CancelledChallenges[0].Id);
        Assert.Equal(OtpTypeId.Email, authentication.BeginCalls[1].Factor);
        Assert.Equal("alice", authentication.BeginCalls[1].Login);
        Assert.Equal("secret", authentication.BeginCalls[1].Password);

        var recoveryRestarted = WhenAuthenticationStops(viewModel);
        viewModel.IsRecoveryCode = true;
        await authentication.WaitForBeginCallsAsync(3);
        await recoveryRestarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(authentication.BeginCalls[2].UseRecoveryCode);
        Assert.Equal(2, authentication.CancelledChallenges.Count);
        Assert.True(viewModel.CanUseRecoveryCode);
        Assert.False(viewModel.CanSelectFactor);
        viewModel.Code = "recovery-code";
        await viewModel.LoginCommand.ExecuteAsync(null);
        Assert.Equal("recovery-code", authentication.CompleteCalls.Single().Code);
        Assert.True(authentication.CompleteCalls.Single().UseRecoveryCode);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task TelegramLogin_OffersOnlyTelegramAndCanRestartWithRecoveryCode()
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.TelegramLogin };
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Waiting, OtpTypeId.Telegram, false,
            [OtpTypeId.Authenticator, OtpTypeId.Email, OtpTypeId.Telegram]));
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Waiting, OtpTypeId.Telegram, true,
            [OtpTypeId.Authenticator, OtpTypeId.Email, OtpTypeId.Telegram]));
        var viewModel = CreateViewModel(authentication);
        viewModel.LoginOrEmail = "alice";
        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(LoginStep.Challenge, viewModel.Step);
        Assert.Empty(authentication.BeginCalls[0].Password);
        Assert.False(viewModel.CanSelectFactor);
        Assert.Equal([OtpTypeId.Telegram], viewModel.AvailableFactors.Select(option => option.Factor));

        var recoveryRestarted = WhenAuthenticationStops(viewModel);
        viewModel.IsRecoveryCode = true;
        await authentication.WaitForBeginCallsAsync(2);
        await recoveryRestarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(authentication.BeginCalls[1].UseRecoveryCode);
        Assert.False(viewModel.CanSelectFactor);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task ApprovedChallenge_CompletesImmediatelyWithEmptyCode()
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.Password };
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Approved, OtpTypeId.Unknown, false));
        var navigation = new TestNavigationService();
        var viewModel = CreateViewModel(authentication, navigation);
        viewModel.LoginOrEmail = "alice";
        await viewModel.LoginCommand.ExecuteAsync(null);
        viewModel.Password = "secret";

        await viewModel.LoginCommand.ExecuteAsync(null);

        var complete = Assert.Single(authentication.CompleteCalls);
        Assert.Empty(complete.Code);
        Assert.False(complete.UseRecoveryCode);
        Assert.Equal(1, navigation.ShowMessengerCalls);
        Assert.Empty(viewModel.Password);
    }

    [Fact]
    public async Task ResetLoginViewModel_ReentryClearsStateAndNavigatesOncePerCommittedAttempt()
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.Password };
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Approved, OtpTypeId.Unknown, false));
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Approved, OtpTypeId.Unknown, false));
        var navigation = new TestNavigationService();
        var viewModel = CreateViewModel(authentication, navigation);

        viewModel.LoginOrEmail = "alice";
        await viewModel.LoginCommand.ExecuteAsync(null);
        viewModel.Password = "first-password";
        await viewModel.LoginCommand.ExecuteAsync(null);
        Assert.Equal(1, navigation.ShowMessengerCalls);

        viewModel.Reset();
        Assert.Equal(LoginStep.Identifier, viewModel.Step);
        Assert.Empty(viewModel.LoginOrEmail);
        Assert.Empty(viewModel.Password);
        Assert.Empty(viewModel.Code);

        viewModel.LoginOrEmail = "alice";
        await viewModel.LoginCommand.ExecuteAsync(null);
        viewModel.Password = "second-password";
        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(2, navigation.ShowMessengerCalls);
        Assert.Equal(2, authentication.CompleteCalls.Count);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task ApprovedChallenge_MissingTokensDoesNotNavigateAndRetriesSameReference()
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.Password };
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Approved, OtpTypeId.Unknown, false));
        authentication.CompleteResults.Enqueue(AuthenticationOperationResult.Failure("Error_LoginMissingTokens"));
        authentication.CompleteResults.Enqueue(AuthenticationOperationResult.Failure("Error_LoginUnavailable"));
        var navigation = new TestNavigationService();
        var viewModel = CreateViewModel(authentication, navigation);
        viewModel.LoginOrEmail = "alice";
        await viewModel.LoginCommand.ExecuteAsync(null);
        viewModel.Password = "secret";
        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal("Error_LoginMissingTokens", viewModel.ErrorMessage);
        Assert.Equal(0, navigation.ShowMessengerCalls);
        Assert.True(viewModel.CanRetry);
        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(2, authentication.CompleteCalls.Count);
        Assert.Equal(authentication.CompleteCalls[0].Reference.Id, authentication.CompleteCalls[1].Reference.Id);
        Assert.Single(authentication.BeginCalls);
        Assert.Equal(0, navigation.ShowMessengerCalls);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task InvalidCode_LeavesChallengeActiveAndPreservesEnteredCode()
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.PasswordSecondFactor };
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Waiting, OtpTypeId.Authenticator, true));
        authentication.CompleteResults.Enqueue(AuthenticationOperationResult.Failure("Error_LoginInvalidCode"));
        var navigation = new TestNavigationService();
        var viewModel = await StartChallengeAsync(authentication, AuthLoginMode.PasswordSecondFactor, navigation);
        viewModel.Code = "wrong-code";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(LoginStep.Challenge, viewModel.Step);
        Assert.False(viewModel.IsChallengeTerminal);
        Assert.Equal("wrong-code", viewModel.Code);
        Assert.Equal("Error_LoginInvalidCode", viewModel.ErrorMessage);
        Assert.Equal(0, navigation.ShowMessengerCalls);
        Assert.Single(authentication.BeginCalls);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task Resend_UsesCooldownAndKeepsButtonVisible()
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.PasswordSecondFactor };
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Waiting, OtpTypeId.Email, true,
            [OtpTypeId.Email]));
        authentication.ResendResults.Enqueue(Challenge(AuthChallengeState.Waiting, OtpTypeId.Email, true,
            [OtpTypeId.Email]));
        var viewModel = await StartChallengeAsync(authentication, AuthLoginMode.PasswordSecondFactor);

        await viewModel.ResendCodeCommand.ExecuteAsync(null);

        Assert.Single(authentication.ResentChallenges);
        Assert.True(viewModel.CanShowResend);
        Assert.False(viewModel.CanResend);
        Assert.False(viewModel.ResendCodeCommand.CanExecute(null));
        Assert.Equal("Login_ResendCooldown", viewModel.ResendCooldownText);
        await viewModel.CleanupAsync();
    }

    [Theory]
    [InlineData(AuthChallengeState.Expired)]
    [InlineData(AuthChallengeState.Rejected)]
    [InlineData(AuthChallengeState.Cancelled)]
    public async Task TerminalChallenge_RetryBeginsAgainWithoutReResolving(AuthChallengeState terminalState)
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.Password };
        authentication.BeginResults.Enqueue(Challenge(terminalState, OtpTypeId.Unknown, false));
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Waiting, OtpTypeId.Unknown, false));
        var viewModel = await StartChallengeAsync(authentication, AuthLoginMode.Password);

        Assert.True(viewModel.IsChallengeTerminal);
        Assert.True(viewModel.CanRetry);
        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(2, authentication.BeginCalls.Count);
        Assert.Single(authentication.ResolvedLogins);
        Assert.False(viewModel.IsChallengeTerminal);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task TelegramWaiting_PollsSequentiallyAndCompletesSameChallengeWhenApproved()
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.TelegramLogin };
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Waiting, OtpTypeId.Telegram, false));
        authentication.PollResults.Enqueue(Challenge(AuthChallengeState.Approved, OtpTypeId.Telegram, false));
        var navigation = new TestNavigationService();
        var viewModel = CreateViewModel(authentication, navigation);
        viewModel.LoginOrEmail = "alice";

        await viewModel.LoginCommand.ExecuteAsync(null);
        await authentication.WaitForPollCallsAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
        await navigation.WaitForMessengerAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var complete = Assert.Single(authentication.CompleteCalls);
        Assert.Equal(authentication.BeginCalls[0].ChallengeReferenceId, complete.Reference.Id);
        Assert.Empty(complete.Code);
        Assert.Equal(1, navigation.ShowMessengerCalls);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task LoginModeMismatch_ReresolvesAndReturnsToIdentifierBeforeNextAction()
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.Password };
        authentication.ResolveResults.Enqueue(SignInResolutionResult.Success(AuthLoginMode.Password));
        authentication.ResolveResults.Enqueue(SignInResolutionResult.Success(AuthLoginMode.PasswordSecondFactor));
        authentication.BeginResults.Enqueue(SignInChallengeResult.Failure("login_mode_disabled", "Error_LoginModeChanged"));
        var viewModel = CreateViewModel(authentication);
        viewModel.LoginOrEmail = "alice";
        await viewModel.LoginCommand.ExecuteAsync(null);
        viewModel.Password = "secret";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(LoginStep.Identifier, viewModel.Step);
        Assert.Empty(viewModel.Password);
        Assert.Equal("Login_ModeChanged", viewModel.StatusMessage);
        Assert.Equal(["alice", "alice"], authentication.ResolvedLogins);
        Assert.Single(authentication.BeginCalls);
        Assert.Equal(AuthLoginMode.PasswordSecondFactor, authentication.LoginMode);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task AccountNotFoundAndUnimplementedResolveDoNotFallBackToLegacyLogin()
    {
        foreach (var error in new[]
        {
            "Error_LoginAccountNotFound",
            "Error_LoginUpdateRequired"
        })
        {
            var authentication = new FakeAuthenticationService();
            authentication.ResolveResults.Enqueue(SignInResolutionResult.Failure(
                error == "Error_LoginAccountNotFound" ? "account_not_found" : null,
                error));
            var viewModel = CreateViewModel(authentication);
            viewModel.LoginOrEmail = "alice";
            viewModel.Password = "stale";
            viewModel.Code = "stale-code";

            await viewModel.LoginCommand.ExecuteAsync(null);

            Assert.Equal(LoginStep.Identifier, viewModel.Step);
            Assert.Empty(viewModel.Password);
            Assert.Empty(viewModel.Code);
            Assert.Equal(error, viewModel.ErrorMessage);
            Assert.Equal(0, authentication.LegacyLoginCalls);
            Assert.Empty(authentication.BeginCalls);
            await viewModel.CleanupAsync();
        }
    }

    [Theory]
    [InlineData("Error_LoginRateLimited")]
    [InlineData("Error_LoginLocked")]
    [InlineData("Error_LoginProtectionUnavailable")]
    public async Task Resolve_IdentityProtectionErrorPreservesResourceKey(string errorResourceKey)
    {
        var authentication = new FakeAuthenticationService();
        authentication.ResolveResults.Enqueue(SignInResolutionResult.Failure("rate_limited", errorResourceKey));
        var viewModel = CreateViewModel(authentication);
        viewModel.LoginOrEmail = "alice";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(errorResourceKey, viewModel.ErrorMessage);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task CompleteNetworkFailure_RetryRepeatsCompleteOnSameReference()
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.PasswordSecondFactor };
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Waiting, OtpTypeId.Authenticator, true));
        authentication.CompleteResults.Enqueue(AuthenticationOperationResult.Failure("Error_LoginUnavailable"));
        authentication.CompleteResults.Enqueue(AuthenticationOperationResult.Failure("Error_LoginInvalidCode"));
        var viewModel = await StartChallengeAsync(authentication, AuthLoginMode.PasswordSecondFactor);
        viewModel.Code = "123456";
        await viewModel.LoginCommand.ExecuteAsync(null);
        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(2, authentication.CompleteCalls.Count);
        Assert.Equal(authentication.CompleteCalls[0].Reference.Id, authentication.CompleteCalls[1].Reference.Id);
        Assert.Equal(["123456", "123456"], authentication.CompleteCalls.Select(call => call.Code));
        Assert.Single(authentication.BeginCalls);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task LateBeginAfterChangeLogin_DoesNotReopenOldChallenge()
    {
        var authentication = new FakeAuthenticationService { LoginMode = AuthLoginMode.Password };
        var lateBegin = new TaskCompletionSource<SignInChallengeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        authentication.BeginHandler = _ => lateBegin.Task;
        var viewModel = CreateViewModel(authentication);
        viewModel.LoginOrEmail = "alice";
        await viewModel.LoginCommand.ExecuteAsync(null);
        viewModel.Password = "secret";

        var login = viewModel.LoginCommand.ExecuteAsync(null);
        await authentication.WaitForBeginCallsAsync(1).WaitAsync(TimeSpan.FromSeconds(2));
        await viewModel.ChangeLoginCommand.ExecuteAsync(null);
        lateBegin.SetResult(Challenge(AuthChallengeState.Waiting, OtpTypeId.Email, true));
        await login;

        Assert.Equal(LoginStep.Identifier, viewModel.Step);
        Assert.Null(viewModel.TelegramUrl);
        Assert.Single(authentication.CancelledAttempts);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public async Task LateQrAcceptedAfterIdentifierLogin_DoesNotNavigateTwiceOrReplaceLogin()
    {
        var authentication = new FakeAuthenticationService
        {
            FastAuthSession = new FastAuthSession(
                "qr-session",
                new FastAuthQrCode(ValidQrPngBase64, DateTimeOffset.UtcNow.AddMinutes(5)))
        };
        var qrGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        authentication.FastAuthStreamGate = qrGate.Task;
        authentication.FastAuthUpdates = [new FastAuthUpdate(FastAuthUpdateKind.Accepted)];
        var navigation = new TestNavigationService();
        var viewModel = CreateViewModel(authentication, navigation);

        var qrLoad = viewModel.LoadFastAuthAsync();
        await authentication.FastAuthSubscribed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.LoginOrEmail = "alice";
        await viewModel.LoginCommand.ExecuteAsync(null);
        authentication.LoginMode = AuthLoginMode.Password;
        viewModel.Password = "secret";
        authentication.BeginResults.Enqueue(Challenge(AuthChallengeState.Approved, OtpTypeId.Unknown, false));
        await viewModel.LoginCommand.ExecuteAsync(null);
        qrGate.SetResult();
        await qrLoad;

        Assert.Equal(1, navigation.ShowMessengerCalls);
        Assert.Equal("Login_Success", viewModel.StatusMessage);
        Assert.Single(authentication.BeginCalls);
        await viewModel.CleanupAsync();
    }

    [Fact]
    public void PasteOtpCodeCommand_SixDigits_FillsUnifiedCodeInput()
    {
        var viewModel = CreateViewModel(new FakeAuthenticationService());

        viewModel.PasteOtpCodeCommand.Execute("123456");

        Assert.Equal("123456", viewModel.Code);
        Assert.Equal("123456", viewModel.OtpCode);
    }

    private static async Task<LoginViewModel> StartChallengeAsync(
        FakeAuthenticationService authentication,
        AuthLoginMode mode,
        TestNavigationService? navigation = null)
    {
        authentication.LoginMode = mode;
        var challenge = Challenge(
            AuthChallengeState.Waiting,
            mode == AuthLoginMode.TelegramLogin ? OtpTypeId.Telegram : OtpTypeId.Authenticator,
            needsCode: true,
            factors: [OtpTypeId.Authenticator, OtpTypeId.Email]);
        authentication.BeginResults.Enqueue(challenge);
        var viewModel = CreateViewModel(authentication, navigation);
        viewModel.LoginOrEmail = "alice";
        await viewModel.LoginCommand.ExecuteAsync(null);
        if (mode != AuthLoginMode.TelegramLogin)
        {
            viewModel.Password = "secret";
            await viewModel.LoginCommand.ExecuteAsync(null);
        }

        return viewModel;
    }

    private static LoginViewModel CreateViewModel(
        IAuthenticationService authentication,
        TestNavigationService? navigation = null) =>
        new(authentication, navigation ?? new TestNavigationService(), new TestLocalizationService());

    private static SignInChallengeResult Challenge(
        AuthChallengeState state,
        OtpTypeId factor,
        bool needsCode,
        IReadOnlyList<OtpTypeId>? factors = null) =>
        SignInChallengeResult.Success(new SignInChallenge(
            new AuthChallengeReference { Id = "first", Secret = "secret" },
            state,
            factor,
            factors ?? [],
            needsCode,
            string.Empty,
            null,
            null));

    private static TaskCompletionSource WhenAuthenticationStops(LoginViewModel viewModel)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == nameof(LoginViewModel.IsAuthenticating) && !viewModel.IsAuthenticating)
            {
                viewModel.PropertyChanged -= handler;
                completion.TrySetResult();
            }
        };
        viewModel.PropertyChanged += handler;
        return completion;
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        private AuthenticationAttempt? _currentAttempt;
        private readonly List<TaskCompletionSource> _beginWaiters = [];
        private readonly List<TaskCompletionSource> _pollWaiters = [];
        private int _pollCalls;

        public AuthLoginMode LoginMode { get; set; } = AuthLoginMode.Password;
        public Queue<SignInResolutionResult> ResolveResults { get; } = [];
        public Queue<SignInChallengeResult> BeginResults { get; } = [];
        public Queue<SignInChallengeResult> PollResults { get; } = [];
        public Queue<SignInChallengeResult> ResendResults { get; } = [];
        public Queue<AuthenticationOperationResult> CompleteResults { get; } = [];
        public Func<BeginCall, Task<SignInChallengeResult>>? BeginHandler { get; set; }
        public List<string> ResolvedLogins { get; } = [];
        public List<BeginCall> BeginCalls { get; } = [];
        public List<CompleteCall> CompleteCalls { get; } = [];
        public List<AuthChallengeReference> CancelledChallenges { get; } = [];
        public List<AuthChallengeReference> ResentChallenges { get; } = [];
        public List<AuthenticationAttempt> CancelledAttempts { get; } = [];
        public int LegacyLoginCalls { get; private set; }
        public FastAuthSession? FastAuthSession { get; init; }
        public IReadOnlyList<FastAuthUpdate> FastAuthUpdates { get; set; } = [];
        public Task? FastAuthStreamGate { get; set; }
        public TaskCompletionSource FastAuthSubscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AuthenticationAttempt? CreateAuthenticationAttempt() => _currentAttempt = AuthenticationAttempt.Create();

        public bool IsCurrentAuthenticationAttempt(AuthenticationAttempt attempt) => _currentAttempt == attempt;

        public Task CancelAuthenticationAttemptAsync(AuthenticationAttempt attempt)
        {
            CancelledAttempts.Add(attempt);
            if (_currentAttempt == attempt)
            {
                _currentAttempt = null;
            }

            return Task.CompletedTask;
        }

        public Task<SignInResolutionResult> ResolveSignInAsync(AuthenticationAttempt attempt, string login, CancellationToken cancellationToken = default)
        {
            ResolvedLogins.Add(login);
            var result = ResolveResults.Count > 0
                ? ResolveResults.Dequeue()
                : SignInResolutionResult.Success(LoginMode);
            if (result.IsSuccess)
            {
                LoginMode = result.LoginMode;
            }

            return Task.FromResult(result);
        }

        public async Task<SignInChallengeResult> BeginSignInAsync(
            AuthenticationAttempt attempt,
            string login,
            string password,
            AuthLoginMode loginMode,
            OtpTypeId factor,
            bool useRecoveryCode,
            CancellationToken cancellationToken = default)
        {
            var call = new BeginCall(attempt, login, password, loginMode, factor, useRecoveryCode);
            BeginCalls.Add(call);
            CompleteWaiters(_beginWaiters, BeginCalls.Count);
            if (BeginHandler is not null)
            {
                var handled = await BeginHandler(call);
                call.ChallengeReferenceId = handled.Challenge?.Reference.Id;
                return handled;
            }

            var result = BeginResults.Count > 0
                ? BeginResults.Dequeue()
                : SignInChallengeResult.Failure(null, "Error_LoginFailed");
            call.ChallengeReferenceId = result.Challenge?.Reference.Id;
            return result;
        }

        public Task<SignInChallengeResult> GetAuthChallengeAsync(AuthenticationAttempt attempt, AuthChallengeReference reference, CancellationToken cancellationToken = default)
        {
            _pollCalls++;
            CompleteWaiters(_pollWaiters, _pollCalls);
            return Task.FromResult(PollResults.Count > 0
                ? PollResults.Dequeue()
                : SignInChallengeResult.Failure(null, "Error_LoginUnavailable"));
        }

        public Task<AuthenticationOperationResult> CompleteAuthChallengeAsync(
            AuthenticationAttempt attempt,
            AuthChallengeReference reference,
            string code,
            bool useRecoveryCode,
            CancellationToken cancellationToken = default)
        {
            CompleteCalls.Add(new CompleteCall(attempt, reference, code, useRecoveryCode));
            return Task.FromResult(CompleteResults.Count > 0
                ? CompleteResults.Dequeue()
                : AuthenticationOperationResult.Success());
        }

        public Task<SignInChallengeResult> ResendAuthChallengeAsync(AuthenticationAttempt attempt, AuthChallengeReference reference, CancellationToken cancellationToken = default)
        {
            ResentChallenges.Add(reference);
            return Task.FromResult(ResendResults.Count > 0
                ? ResendResults.Dequeue()
                : SignInChallengeResult.Failure(null, "Error_LoginUnavailable"));
        }

        public Task<AuthenticationOperationResult> CancelAuthChallengeAsync(AuthenticationAttempt attempt, AuthChallengeReference reference, CancellationToken cancellationToken = default)
        {
            CancelledChallenges.Add(reference);
            return Task.FromResult(AuthenticationOperationResult.Success());
        }

        public Task<FastAuthSession?> CreateFastAuthSessionAsync(AuthenticationAttempt attempt, CancellationToken cancellationToken = default) =>
            Task.FromResult(FastAuthSession);

        public async IAsyncEnumerable<FastAuthUpdate> SubscribeFastAuthAsync(
            AuthenticationAttempt attempt,
            FastAuthSession session,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            FastAuthSubscribed.TrySetResult();
            if (FastAuthStreamGate is not null)
            {
                await FastAuthStreamGate;
            }

            foreach (var update in FastAuthUpdates)
            {
                if (update.Kind == FastAuthUpdateKind.Accepted && _currentAttempt == attempt)
                {
                    _currentAttempt = null;
                }

                yield return update;
            }
        }

        public Task<LoginResult> LoginAsync(string loginOrEmail, string password, string otpCode, CancellationToken cancellationToken = default)
        {
            LegacyLoginCalls++;
            return Task.FromResult(LoginResult.Failure("Error_LoginFailed"));
        }

        public Task<RegistrationStartResult> StartRegistrationAsync(string firstName, string lastName, string username, string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(RegistrationStartResult.Failure("Error_RegistrationFailed"));

        public Task<AuthenticationOperationResult> ConfirmRegistrationAsync(string codeId, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationOperationResult.Failure("Error_RegistrationCodeInvalid"));

        public Task<AuthenticationOperationResult> SetPasswordAsync(string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationOperationResult.Failure("Error_PasswordInvalid"));

        public Task<PasswordResetStartResult> StartPasswordResetAsync(string loginOrEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(PasswordResetStartResult.Failure("Error_PasswordResetFailed"));

        public Task<AuthenticationOperationResult> CompletePasswordResetAsync(string resetId, string code, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationOperationResult.Failure("Error_PasswordResetCodeInvalid"));

        public Task WaitForBeginCallsAsync(int count)
        {
            if (BeginCalls.Count >= count)
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _beginWaiters.Add(completion);
            return completion.Task;
        }

        public Task WaitForPollCallsAsync(int count)
        {
            if (_pollCalls >= count)
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pollWaiters.Add(completion);
            return completion.Task;
        }

        private static void CompleteWaiters(List<TaskCompletionSource> waiters, int count)
        {
            foreach (var waiter in waiters.ToArray())
            {
                waiter.TrySetResult();
                waiters.Remove(waiter);
            }
        }
    }

    private sealed class BeginCall(
        AuthenticationAttempt attempt,
        string login,
        string password,
        AuthLoginMode loginMode,
        OtpTypeId factor,
        bool useRecoveryCode)
    {
        public AuthenticationAttempt Attempt { get; } = attempt;
        public string Login { get; } = login;
        public string Password { get; } = password;
        public AuthLoginMode LoginMode { get; } = loginMode;
        public OtpTypeId Factor { get; } = factor;
        public bool UseRecoveryCode { get; } = useRecoveryCode;
        public string? ChallengeReferenceId { get; set; }
    }

    private sealed record CompleteCall(
        AuthenticationAttempt Attempt,
        AuthChallengeReference Reference,
        string Code,
        bool UseRecoveryCode);

    private sealed class TestNavigationService : IOnboardingNavigationService
    {
        private readonly TaskCompletionSource _messengerShown = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        public void ShowMessenger()
        {
            ShowMessengerCalls++;
            _messengerShown.TrySetResult();
        }
        public Task WaitForMessengerAsync() => _messengerShown.Task;
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public string ResolveSupportedLanguage(string? requestedLanguage) => "en";
        public void Apply(string language) { }
        public string GetString(string resourceKey) => resourceKey;
    }
}
