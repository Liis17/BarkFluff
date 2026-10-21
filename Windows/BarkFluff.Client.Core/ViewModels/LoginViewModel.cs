using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Identity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public enum LoginStep
{
    Identifier,
    Password,
    Challenge
}

public sealed partial class LoginViewModel : ObservableObject
{
    private static readonly TimeSpan ChallengePollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    private readonly IAuthenticationService _authenticationService;
    private readonly IOnboardingNavigationService _navigation;
    private readonly ILocalizationService _localization;
    private readonly TimeProvider _timeProvider;

    private AuthenticationAttempt? _attempt;
    private Task _resetCleanupTask = Task.CompletedTask;
    private CancellationTokenSource? _flowCancellation;
    private CancellationTokenSource? _fastAuthCancellation;
    private CancellationTokenSource? _challengePollingCancellation;
    private Task? _challengePollingTask;
    private CancellationTokenSource? _challengeClockCancellation;
    private CancellationTokenSource? _resendClockCancellation;
    private SignInChallenge? _challenge;
    private AuthLoginMode _loginMode;
    private string _resolvedLogin = string.Empty;
    private DateTimeOffset _resendAvailableAt;
    private RetryOperation _retryOperation;
    private int _fastAuthGeneration;
    private int _flowGeneration;
    private bool _isApplyingChallenge;
    private bool _hasNavigated;

    public LoginViewModel(
        IAuthenticationService authenticationService,
        IOnboardingNavigationService navigation,
        ILocalizationService localization,
        TimeProvider? timeProvider = null)
    {
        _authenticationService = authenticationService;
        _navigation = navigation;
        _localization = localization;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _loginOrEmail = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private LoginStep _step = LoginStep.Identifier;

    [ObservableProperty]
    private bool _isAuthenticating;

    [ObservableProperty]
    private bool _requiresTwoFactor;

    private bool _isRecoveryCode;

    public bool IsRecoveryCode
    {
        get => _isRecoveryCode;
        set
        {
            if (IsAuthenticating && !_isApplyingChallenge) return;
            if (SetProperty(ref _isRecoveryCode, value)) OnIsRecoveryCodeChanged(value);
        }
    }

    [ObservableProperty]
    private bool _isChallengeTerminal;

    [ObservableProperty]
    private bool _isFastAuthLoading;

    [ObservableProperty]
    private string? _fastAuthQrCode;

    [ObservableProperty]
    private string? _fastAuthStatusMessage;

    [ObservableProperty]
    private string _fastAuthCountdown = string.Empty;

    [ObservableProperty]
    private string? _telegramUrl;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    private OtpTypeId _selectedFactor = OtpTypeId.Unknown;

    public OtpTypeId SelectedFactor
    {
        get => _selectedFactor;
        set
        {
            if (IsAuthenticating && !_isApplyingChallenge) return;
            if (SetProperty(ref _selectedFactor, value)) OnSelectedFactorChanged(value);
        }
    }

    public ObservableCollection<LoginFactorOption> AvailableFactors { get; } = [];

    public bool IsIdentifierStep => Step == LoginStep.Identifier;
    public bool IsPasswordStep => Step == LoginStep.Password;
    public bool IsChallengeStep => Step == LoginStep.Challenge;
    public bool CanSubmitChallenge => !IsChallengeStep || RequiresTwoFactor;
    public bool IsResolvedIdentifier => Step != LoginStep.Identifier;
    public bool HasTelegramUrl => !string.IsNullOrWhiteSpace(TelegramUrl);
    public bool CanUseRecoveryCode => _loginMode is AuthLoginMode.TelegramLogin or AuthLoginMode.PasswordSecondFactor;
    public bool CanChangeRecoveryCode => CanUseRecoveryCode && !IsAuthenticating && !IsChallengeTerminal;
    public bool CanSelectFactor => IsChallengeStep
        && _loginMode == AuthLoginMode.PasswordSecondFactor
        && !IsRecoveryCode
        && !IsAuthenticating
        && !IsChallengeTerminal
        && AvailableFactors.Count >= 2;
    public bool CanRetry => _retryOperation != RetryOperation.None
        && !IsAuthenticating
        && (_retryOperation != RetryOperation.Resend || _timeProvider.GetUtcNow() >= _resendAvailableAt);
    public bool CanShowResend => IsChallengeStep
        && !IsChallengeTerminal
        && !IsRecoveryCode
        && _challenge?.Factor is OtpTypeId.Email or OtpTypeId.Telegram;
    public bool CanResend => CanShowResend
        && _timeProvider.GetUtcNow() >= _resendAvailableAt
        && !IsAuthenticating;
    public string SubmitButtonText => _localization.GetString(Step switch
    {
        LoginStep.Identifier => "Login_Continue",
        LoginStep.Password => "Login_SignIn",
        _ => "Login_Confirm"
    });
    public string CodePlaceholder => _localization.GetString(IsRecoveryCode ? "Login_RecoveryCodePlaceholder" : "Login_CodePlaceholder");
    public string CodeHint => _localization.GetString(IsRecoveryCode ? "Login_RecoveryCodeHint" : "Login_CodeHint");
    public string ChallengeExpiryText => _challenge?.ExpiresAt is { } expiresAt
        ? string.Format(_localization.GetString("Login_ChallengeExpires"), expiresAt.ToLocalTime().ToString("HH:mm"))
        : string.Empty;
    public string ResendCooldownText => _timeProvider.GetUtcNow() < _resendAvailableAt
        ? string.Format(_localization.GetString("Login_ResendCooldown"), Math.Ceiling((_resendAvailableAt - _timeProvider.GetUtcNow()).TotalSeconds))
        : _localization.GetString("Login_ResendCode");

    // Kept as a small compatibility surface for the existing paste command and tests.
    public string OtpCode => Code;

    public void Reset()
    {
        StopFastAuthUi();
        _resetCleanupTask = Task.WhenAll(_resetCleanupTask, CancelCurrentAttemptAsync());
        LoginOrEmail = string.Empty;
        Password = string.Empty;
        Code = string.Empty;
        Step = LoginStep.Identifier;
        _loginMode = AuthLoginMode.LoginModeUnspecified;
        OnPropertyChanged(nameof(CanUseRecoveryCode));
        _resolvedLogin = string.Empty;
        _challenge = null;
        IsRecoveryCode = false;
        IsChallengeTerminal = false;
        RequiresTwoFactor = false;
        SelectedFactor = OtpTypeId.Unknown;
        TelegramUrl = null;
        AvailableFactors.Clear();
        ErrorMessage = null;
        StatusMessage = null;
        SetRetryOperation(RetryOperation.None);
        _hasNavigated = false;
        NotifyStepPropertiesChanged();
    }

    public void StopFastAuth()
    {
        StopFastAuthUi();
        _ = CancelCurrentAttemptAsync();
    }

    public void ClearSensitiveState()
    {
        Password = string.Empty;
        Code = string.Empty;
        RequiresTwoFactor = false;
    }

    public async Task CleanupAsync()
    {
        StopFastAuthUi();
        await CancelCurrentAttemptAsync();
        ClearSensitiveState();
    }

    public async Task LoadFastAuthAsync()
    {
        var flowGeneration = _flowGeneration;
        StopFastAuthUi();
        await _resetCleanupTask;
        if (flowGeneration != _flowGeneration) return;
        _resetCleanupTask = Task.CompletedTask;
        var cleanup = CancelCurrentAttemptAsync();
        flowGeneration = _flowGeneration;
        await cleanup;
        if (flowGeneration != _flowGeneration) return;

        var attempt = _authenticationService.CreateAuthenticationAttempt();
        if (attempt is null)
        {
            FastAuthStatusMessage = _localization.GetString("Error_FastAuthUnavailable");
            return;
        }

        _attempt = attempt;
        _flowCancellation = new CancellationTokenSource();
        var cancellation = new CancellationTokenSource();
        _fastAuthCancellation = cancellation;
        var generation = Interlocked.Increment(ref _fastAuthGeneration);
        await RunFastAuthAsync(attempt, cancellation, generation);
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        if (!CanLogin())
        {
            return;
        }

        var generation = _flowGeneration;
        var cancellationToken = _flowCancellation?.Token ?? CancellationToken.None;
        ErrorMessage = null;
        StatusMessage = null;
        IsAuthenticating = true;
        try
        {
            switch (Step)
            {
                case LoginStep.Identifier:
                    await ResolveIdentifierAsync();
                    break;
                case LoginStep.Password:
                    await BeginSignInAsync();
                    break;
                case LoginStep.Challenge:
                    await StopChallengePollingAsync();
                    await CompleteChallengeAsync(Code, fromPolling: false);
                    break;
            }
        }
        catch (OperationCanceledException) when (generation != _flowGeneration || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _flowGeneration) return;
            ErrorMessage = _localization.GetString("Error_LoginUnavailable");
            SetRetryOperation(RetryOperationForCurrentStep());
        }
        finally
        {
            if (generation == _flowGeneration) IsAuthenticating = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryAsync()
    {
        if (!CanRetry) return;
        var generation = _flowGeneration;
        var cancellationToken = _flowCancellation?.Token ?? CancellationToken.None;
        var operation = _retryOperation;
        if (operation == RetryOperation.None)
        {
            return;
        }

        SetRetryOperation(RetryOperation.None);
        ErrorMessage = null;
        IsAuthenticating = true;
        try
        {
            switch (operation)
            {
                case RetryOperation.Resolve:
                    StopFastAuthUi();
                    await ResolveCurrentIdentifierAsync();
                    break;
                case RetryOperation.Begin:
                    await BeginSignInAsync();
                    break;
                case RetryOperation.Complete:
                    await CompleteChallengeAsync(Code, fromPolling: false);
                    break;
                case RetryOperation.Resend:
                    await ResendCodeCoreAsync(retry: true);
                    break;
                case RetryOperation.RestartChallenge:
                    await RestartChallengeAsync(retry: true);
                    break;
            }
        }
        catch (OperationCanceledException) when (generation != _flowGeneration || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _flowGeneration) return;
            ErrorMessage = _localization.GetString("Error_LoginUnavailable");
            SetRetryOperation(operation);
        }
        finally
        {
            if (generation == _flowGeneration) IsAuthenticating = false;
        }
    }

    [RelayCommand]
    private async Task ChangeLoginAsync()
    {
        var cleanup = CancelCurrentAttemptAsync();
        var generation = _flowGeneration;
        await cleanup;
        if (generation != _flowGeneration) return;
        StopFastAuthUi();
        ClearSensitiveState();
        _challenge = null;
        _loginMode = AuthLoginMode.LoginModeUnspecified;
        _resolvedLogin = string.Empty;
        TelegramUrl = null;
        AvailableFactors.Clear();
        IsRecoveryCode = false;
        IsChallengeTerminal = false;
        Step = LoginStep.Identifier;
        ErrorMessage = null;
        StatusMessage = null;
        SetRetryOperation(RetryOperation.None);
        _ = LoadFastAuthAsync();
    }

    [RelayCommand]
    private void PasteOtpCode(string? value)
    {
        if (IsRecoveryCode)
        {
            Code = (value ?? string.Empty).Trim();
            return;
        }
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).Take(6).ToArray());
        if (digits.Length == 6)
        {
            Code = digits;
        }
    }

    [RelayCommand]
    private void ChooseAnotherNode() => _navigation.ShowSelectNode();

    [RelayCommand]
    private void ShowPasswordRecovery() => _navigation.ShowPasswordRecovery();

    [RelayCommand]
    private void ShowRegistration() => _navigation.ShowRegistration();

    [RelayCommand(CanExecute = nameof(CanResend))]
    private Task ResendCodeAsync() => ResendCodeCoreAsync(retry: false);

    private async Task ResendCodeCoreAsync(bool retry)
    {
        if (_attempt is null || _challenge is null || (!retry && !CanResend))
        {
            return;
        }

        var attempt = _attempt;
        var challenge = _challenge;
        var generation = _flowGeneration;
        var cancellationToken = _flowCancellation?.Token ?? CancellationToken.None;
        ErrorMessage = null;
        IsAuthenticating = true;
        SetRetryOperation(RetryOperation.None);

        try
        {
            await StopChallengePollingAsync();
            var result = await _authenticationService.ResendAuthChallengeAsync(
                attempt,
                challenge.Reference,
                cancellationToken);
            if (generation != _flowGeneration || !IsCurrentAttempt(attempt)) return;
            if (!result.IsSuccess || result.Challenge is null)
            {
                ErrorMessage = _localization.GetString(result.ErrorResourceKey ?? "Error_LoginUnavailable");
                SetRetryOperation(RetryOperation.Resend);
                return;
            }

            SetResendCooldown(_timeProvider.GetUtcNow() + ResendCooldown);
            ApplyChallenge(result.Challenge, restartClock: true);
            StartTelegramPollingIfNeeded();
        }
        catch (OperationCanceledException) when (generation != _flowGeneration || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _flowGeneration) return;
            ErrorMessage = _localization.GetString("Error_LoginUnavailable");
            SetRetryOperation(RetryOperation.Resend);
        }
        finally
        {
            if (generation == _flowGeneration)
            {
                IsAuthenticating = false;
                if (IsCurrentAttempt(attempt)) StartTelegramPollingIfNeeded();
            }
        }
    }

    private async Task ResolveIdentifierAsync()
    {
        var generation = _flowGeneration;
        _resolvedLogin = LoginOrEmail.Trim();
        if (_resolvedLogin.Length == 0)
        {
            ErrorMessage = _localization.GetString("Error_LoginAccountNotFound");
            return;
        }

        StopFastAuthUi();
        await CancelCurrentAttemptAsync(invalidateUi: false);
        if (generation != _flowGeneration) return;
        _attempt = _authenticationService.CreateAuthenticationAttempt();
        if (_attempt is null)
        {
            ErrorMessage = _localization.GetString("Error_LoginUnavailable");
            return;
        }

        _flowCancellation = new CancellationTokenSource();
        _isApplyingChallenge = true;
        Password = string.Empty;
        Code = string.Empty;
        IsRecoveryCode = false;
        SelectedFactor = OtpTypeId.Unknown;
        _isApplyingChallenge = false;
        SetRetryOperation(RetryOperation.Resolve);
        await ResolveCurrentIdentifierAsync();
    }

    private async Task ResolveCurrentIdentifierAsync()
    {
        if (_attempt is null)
        {
            SetRetryOperation(RetryOperation.Resolve);
            ErrorMessage = _localization.GetString("Error_LoginUnavailable");
            return;
        }

        var attempt = _attempt;
        var result = await _authenticationService.ResolveSignInAsync(
            attempt,
            _resolvedLogin,
            _flowCancellation?.Token ?? CancellationToken.None);
        if (!IsCurrentAttempt(attempt))
        {
            return;
        }

        if (!result.IsSuccess)
        {
            ClearSensitiveState();
            Step = LoginStep.Identifier;
            ErrorMessage = _localization.GetString(result.ErrorResourceKey ?? "Error_LoginFailed");
            SetRetryOperation(result.ErrorResourceKey == "Error_LoginUnavailable" ? RetryOperation.Resolve : RetryOperation.None);
            RestartFastAuthForCurrentAttempt();

            return;
        }

        _loginMode = result.LoginMode;
        OnPropertyChanged(nameof(CanUseRecoveryCode));
        Password = string.Empty;
        Code = string.Empty;
        SetRetryOperation(RetryOperation.None);
        if (result.LoginMode == AuthLoginMode.TelegramLogin)
        {
            await BeginSignInAsync();
        }
        else
        {
            Step = LoginStep.Password;
        }
    }

    private async Task BeginSignInAsync()
    {
        if (_attempt is null || _loginMode == AuthLoginMode.LoginModeUnspecified)
        {
            ErrorMessage = _localization.GetString("Error_LoginUnavailable");
            SetRetryOperation(RetryOperation.Resolve);
            return;
        }

        var attempt = _attempt;
        var generation = _flowGeneration;
        var cancellationToken = _flowCancellation?.Token ?? CancellationToken.None;
        await StopChallengePollingAsync();
        if (generation != _flowGeneration || !IsCurrentAttempt(attempt)) return;
        var requestedFactor = _loginMode == AuthLoginMode.TelegramLogin
            ? OtpTypeId.Telegram
            : SelectedFactor;
        SetRetryOperation(RetryOperation.Begin);
        var result = await _authenticationService.BeginSignInAsync(
            attempt,
            _resolvedLogin,
            _loginMode == AuthLoginMode.TelegramLogin ? string.Empty : Password,
            _loginMode,
            requestedFactor,
            IsRecoveryCode,
            cancellationToken);
        if (!IsCurrentAttempt(attempt))
        {
            return;
        }

        if (!result.IsSuccess || result.Challenge is null)
        {
            if (result.ErrorCode is "account_not_found" or "login_mode_disabled")
            {
                await RefreshModeAfterMismatchAsync(attempt);
                return;
            }

            ErrorMessage = _localization.GetString(result.ErrorResourceKey ?? "Error_LoginFailed");
            SetRetryOperation(_challenge is null && IsChallengeStep
                || result.ErrorResourceKey == "Error_LoginUnavailable"
                || result.ErrorResourceKey == "Error_LoginUpdateRequired"
                ? RetryOperation.Begin
                : RetryOperation.None);
            return;
        }

        ErrorMessage = null;
        SetRetryOperation(RetryOperation.None);
        ResetResendCooldown();
        ApplyChallenge(result.Challenge, restartClock: true);
        if (CanShowResend && !IsChallengeTerminal)
        {
            SetResendCooldown(_timeProvider.GetUtcNow() + ResendCooldown);
        }

        if (result.Challenge.State is AuthChallengeState.Expired or AuthChallengeState.Rejected or AuthChallengeState.Cancelled)
        {
            MarkChallengeTerminal(result.Challenge.State);
            return;
        }

        if (result.Challenge.State is AuthChallengeState.Approved or AuthChallengeState.Completed
            && result.Challenge.NeedsCode == false
            && !IsRecoveryCode)
        {
            await CompleteChallengeAsync(string.Empty, fromPolling: false);
            return;
        }

        StartTelegramPollingIfNeeded();
    }

    private async Task RefreshModeAfterMismatchAsync(AuthenticationAttempt attempt)
    {
        var generation = _flowGeneration;
        await StopChallengePollingAsync();
        if (generation != _flowGeneration || !IsCurrentAttempt(attempt)) return;
        StopChallengeClock();
        StopResendClock();
        ResetResendCooldown();
        _challenge = null;
        ClearSensitiveState();
        IsRecoveryCode = false;
        IsChallengeTerminal = false;
        TelegramUrl = null;
        AvailableFactors.Clear();
        Step = LoginStep.Identifier;
        ErrorMessage = null;
        SetRetryOperation(RetryOperation.None);
        StatusMessage = _localization.GetString("Login_ModeChanged");

        var resolution = await _authenticationService.ResolveSignInAsync(
            attempt,
            _resolvedLogin,
            _flowCancellation?.Token ?? CancellationToken.None);
        if (IsCurrentAttempt(attempt) && resolution.IsSuccess)
        {
            _loginMode = resolution.LoginMode;
            OnPropertyChanged(nameof(CanUseRecoveryCode));
        }
        else if (IsCurrentAttempt(attempt) && resolution.ErrorResourceKey is not null)
        {
            StatusMessage = _localization.GetString(resolution.ErrorResourceKey);
        }
        if (IsCurrentAttempt(attempt)) RestartFastAuthForCurrentAttempt();
    }

    private async Task RestartChallengeAsync(bool retry = false)
    {
        if (_attempt is null || _challenge is null || (IsAuthenticating && !retry) || IsChallengeTerminal)
        {
            return;
        }

        var attempt = _attempt;
        var generation = _flowGeneration;
        var cancellationToken = _flowCancellation?.Token ?? CancellationToken.None;
        var oldChallenge = _challenge;
        var oldFactor = oldChallenge.Factor;
        var oldRecovery = _challengeUsesRecoveryCode;
        IsAuthenticating = true;
        ErrorMessage = null;
        SetRetryOperation(RetryOperation.None);
        await StopChallengePollingAsync();

        try
        {
            var cancelled = await _authenticationService.CancelAuthChallengeAsync(
                attempt,
                oldChallenge.Reference,
                cancellationToken);
            if (generation != _flowGeneration || !IsCurrentAttempt(attempt)) return;
            if (!cancelled.IsSuccess)
            {
                _isApplyingChallenge = true;
                SelectedFactor = oldFactor;
                IsRecoveryCode = oldRecovery;
                _isApplyingChallenge = false;
                ErrorMessage = _localization.GetString(cancelled.ErrorResourceKey ?? "Error_LoginUnavailable");
                SetRetryOperation(RetryOperation.RestartChallenge);
                StartTelegramPollingIfNeeded();
                return;
            }

            _challenge = null;
            StopChallengeClock();
            StopResendClock();
            Code = string.Empty;
            IsChallengeTerminal = false;
            await BeginSignInAsync();
        }
        catch (OperationCanceledException) when (generation != _flowGeneration || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _flowGeneration) return;
            ErrorMessage = _localization.GetString("Error_LoginUnavailable");
            SetRetryOperation(_challenge is null ? RetryOperation.Begin : RetryOperation.RestartChallenge);
        }
        finally
        {
            if (generation == _flowGeneration) IsAuthenticating = false;
        }
    }

    private async Task CompleteChallengeAsync(string code, bool fromPolling)
    {
        if (_attempt is null || _challenge is null || IsChallengeTerminal)
        {
            return;
        }

        var attempt = _attempt;
        var generation = _flowGeneration;
        var cancellationToken = _flowCancellation?.Token ?? CancellationToken.None;
        var challenge = _challenge;
        if (!fromPolling)
        {
            IsAuthenticating = true;
        }

        ErrorMessage = null;
        SetRetryOperation(RetryOperation.Complete);
        try
        {
            var result = await _authenticationService.CompleteAuthChallengeAsync(
                attempt,
                challenge.Reference,
                code,
                IsRecoveryCode,
                cancellationToken);
            if (generation != _flowGeneration || !ReferenceEquals(_attempt, attempt)
                || cancellationToken.IsCancellationRequested || (!IsCurrentAttempt(attempt) && !result.IsSuccess))
            {
                return;
            }

            if (!result.IsSuccess)
            {
                var errorKey = result.ErrorResourceKey ?? "Error_LoginFailed";
                ErrorMessage = _localization.GetString(errorKey);
                if (errorKey is "Error_LoginChallengeExpired" or "Error_LoginChallengeRejected" or "Error_LoginChallengeCancelled")
                {
                    MarkChallengeTerminal(errorKey switch
                    {
                        "Error_LoginChallengeExpired" => AuthChallengeState.Expired,
                        "Error_LoginChallengeRejected" => AuthChallengeState.Rejected,
                        _ => AuthChallengeState.Cancelled
                    });
                }
                else if (errorKey is "Error_LoginInvalidCode" or "Error_LoginMissingTokens")
                {
                    SetRetryOperation(errorKey == "Error_LoginMissingTokens" ? RetryOperation.Complete : RetryOperation.None);
                }
                else
                {
                    SetRetryOperation(RetryOperation.Complete);
                }

                return;
            }

            SetRetryOperation(RetryOperation.None);
            await CompleteLoginUiAsync(fromPolling, generation);
        }
        catch (OperationCanceledException) when (generation != _flowGeneration || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _flowGeneration) return;
            ErrorMessage = _localization.GetString("Error_LoginUnavailable");
            SetRetryOperation(RetryOperation.Complete);
        }
        finally
        {
            if (!fromPolling && generation == _flowGeneration)
            {
                IsAuthenticating = false;
            }
        }
    }

    private async Task CompleteLoginUiAsync(bool fromPolling, int generation)
    {
        if (fromPolling)
        {
            _challengePollingCancellation?.Cancel();
        }
        else
        {
            await StopChallengePollingAsync();
        }
        if (generation != _flowGeneration) return;
        StopChallengeClock();
        StopResendClock();
        StopFastAuthUi();
        _attempt = null;
        _flowCancellation?.Cancel();
        _flowCancellation?.Dispose();
        _flowCancellation = null;
        ClearSensitiveState();
        _challenge = null;
        TelegramUrl = null;
        AvailableFactors.Clear();
        IsChallengeTerminal = false;
        OnPropertyChanged(nameof(HasTelegramUrl));
        OnPropertyChanged(nameof(CanSelectFactor));
        OnPropertyChanged(nameof(CanShowResend));
        OnPropertyChanged(nameof(CanResend));
        StatusMessage = _localization.GetString("Login_Success");
        if (!_hasNavigated)
        {
            _hasNavigated = true;
            _navigation.ShowMessenger();
        }
    }

    private async Task RunFastAuthAsync(AuthenticationAttempt attempt, CancellationTokenSource cancellation, int generation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsCurrentFastAuthGeneration(generation))
            {
                IsFastAuthLoading = true;
                FastAuthQrCode = null;
                FastAuthCountdown = string.Empty;
                FastAuthStatusMessage = _localization.GetString("FastAuth_Creating");
                var session = await _authenticationService.CreateFastAuthSessionAsync(attempt, cancellationToken);
                if (!IsCurrentFastAuthGeneration(generation) || !IsCurrentAttempt(attempt))
                {
                    return;
                }

                if (session is null)
                {
                    FastAuthStatusMessage = _localization.GetString("Error_FastAuthUnavailable");
                    return;
                }

                FastAuthQrCode = ValidateBase64Png(session.QrCode.Base64Png);
                if (FastAuthQrCode is null)
                {
                    FastAuthStatusMessage = _localization.GetString("Error_FastAuthFailed");
                    return;
                }

                IsFastAuthLoading = false;
                FastAuthStatusMessage = _localization.GetString("FastAuth_WaitingConfirmation");
                using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var countdownTask = UpdateFastAuthCountdownAsync(session, generation, sessionCancellation.Token);
                FastAuthUpdateKind terminalStatus;
                try
                {
                    terminalStatus = await ObserveFastAuthAsync(attempt, session, generation, sessionCancellation.Token);
                }
                finally
                {
                    sessionCancellation.Cancel();
                    await IgnoreCancellationAsync(countdownTask);
                }
                if (terminalStatus == FastAuthUpdateKind.Accepted || terminalStatus == FastAuthUpdateKind.Failed)
                {
                    return;
                }

                await Task.Delay(
                    terminalStatus == FastAuthUpdateKind.Rejected ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(500),
                    _timeProvider,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (IsCurrentFastAuthGeneration(generation))
            {
                FastAuthQrCode = null;
                FastAuthCountdown = string.Empty;
                FastAuthStatusMessage = _localization.GetString("Error_FastAuthUnavailable");
            }
        }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(_fastAuthCancellation, cancellation))
            {
                _fastAuthCancellation = null;
                IsFastAuthLoading = false;
            }

            if (!ReferenceEquals(_attempt, attempt))
            {
                await _authenticationService.CancelAuthenticationAttemptAsync(attempt);
            }
        }
    }

    private async Task<FastAuthUpdateKind> ObserveFastAuthAsync(
        AuthenticationAttempt attempt,
        FastAuthSession session,
        int generation,
        CancellationToken cancellationToken)
    {
        await foreach (var update in _authenticationService.SubscribeFastAuthAsync(attempt, session, cancellationToken))
        {
            if (!IsCurrentFastAuthGeneration(generation)
                || cancellationToken.IsCancellationRequested || !ReferenceEquals(_attempt, attempt)
                || (!IsCurrentAttempt(attempt) && update.Kind != FastAuthUpdateKind.Accepted))
            {
                return FastAuthUpdateKind.Failed;
            }

            switch (update.Kind)
            {
                case FastAuthUpdateKind.Scanned:
                    FastAuthStatusMessage = _localization.GetString("FastAuth_Scanned");
                    break;
                case FastAuthUpdateKind.Accepted:
                    StatusMessage = _localization.GetString("Login_Success");
                    _attempt = null;
                    _flowCancellation?.Dispose();
                    _flowCancellation = null;
                    if (!_hasNavigated)
                    {
                        _hasNavigated = true;
                        _navigation.ShowMessenger();
                    }
                    return update.Kind;
                case FastAuthUpdateKind.Rejected:
                    FastAuthStatusMessage = _localization.GetString("FastAuth_Rejected");
                    return update.Kind;
                case FastAuthUpdateKind.Expired:
                    FastAuthStatusMessage = _localization.GetString("FastAuth_Expired");
                    return update.Kind;
                case FastAuthUpdateKind.Failed:
                    FastAuthQrCode = null;
                    FastAuthCountdown = string.Empty;
                    FastAuthStatusMessage = _localization.GetString(update.ErrorResourceKey ?? "Error_FastAuthFailed");
                    return update.Kind;
            }
        }

        return FastAuthUpdateKind.Failed;
    }

    private async Task PollTelegramChallengeAsync(AuthenticationAttempt attempt, SignInChallenge challenge, CancellationToken cancellationToken)
    {
        var generation = _flowGeneration;
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsCurrentAttempt(attempt) && ReferenceEquals(_challenge, challenge))
            {
                if (challenge.ExpiresAt is { } expiresAt && _timeProvider.GetUtcNow() >= expiresAt)
                {
                    if (_retryOperation != RetryOperation.Complete) MarkChallengeTerminal(AuthChallengeState.Expired);
                    return;
                }

                await Task.Delay(ChallengePollInterval, _timeProvider, cancellationToken);
                if (cancellationToken.IsCancellationRequested || !IsCurrentAttempt(attempt) || !ReferenceEquals(_challenge, challenge))
                {
                    return;
                }

                SignInChallengeResult result;
                try
                {
                    result = await _authenticationService.GetAuthChallengeAsync(attempt, challenge.Reference, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    if (generation == _flowGeneration && IsCurrentAttempt(attempt) && !cancellationToken.IsCancellationRequested)
                        StatusMessage = _localization.GetString("Login_ChallengeReconnecting");
                    continue;
                }

                if (generation != _flowGeneration || cancellationToken.IsCancellationRequested || !IsCurrentAttempt(attempt)) return;
                if (!result.IsSuccess || result.Challenge is null)
                {
                    StatusMessage = _localization.GetString("Login_ChallengeReconnecting");
                    continue;
                }

                ApplyChallenge(result.Challenge, restartClock: false, clearCode: false);
                challenge = result.Challenge;
                if (challenge.State is AuthChallengeState.Expired or AuthChallengeState.Rejected or AuthChallengeState.Cancelled)
                {
                    MarkChallengeTerminal(challenge.State);
                    return;
                }

                if (challenge.State is AuthChallengeState.Approved or AuthChallengeState.Completed
                    && !IsRecoveryCode)
                {
                    IsAuthenticating = true;
                    await CompleteChallengeAsync(string.Empty, fromPolling: true);
                    if (generation == _flowGeneration) IsAuthenticating = false;
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            var pollingCancellation = _challengePollingCancellation;
            if (pollingCancellation is not null && pollingCancellation.Token == cancellationToken)
            {
                _challengePollingCancellation = null;
                _challengePollingTask = null;
                pollingCancellation.Dispose();
            }
        }
    }

    private void StartTelegramPollingIfNeeded()
    {
        if (_attempt is null || _challenge is null || _loginMode != AuthLoginMode.TelegramLogin
            || IsRecoveryCode || IsChallengeTerminal || _challenge.State != AuthChallengeState.Waiting)
        {
            return;
        }

        if (_challengePollingTask is { IsCompleted: false })
        {
            return;
        }

        _challengePollingCancellation?.Dispose();
        _challengePollingCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _flowCancellation?.Token ?? CancellationToken.None);
        var attempt = _attempt;
        var challenge = _challenge;
        _challengePollingTask = PollTelegramChallengeAsync(attempt, challenge, _challengePollingCancellation.Token);
    }

    private async Task StopChallengePollingAsync()
    {
        var cancellation = _challengePollingCancellation;
        var pollingTask = _challengePollingTask;
        _challengePollingCancellation = null;
        _challengePollingTask = null;
        if (cancellation is null && pollingTask is null)
        {
            return;
        }

        cancellation?.Cancel();
        if (pollingTask is not null)
        {
            try
            {
                await pollingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
    }

    private void StartChallengeClock()
    {
        StopChallengeClock();
        if (_challenge?.ExpiresAt is not { } expiresAt)
        {
            return;
        }

        _challengeClockCancellation = new CancellationTokenSource();
        var cancellationToken = _challengeClockCancellation.Token;
        _ = UpdateChallengeClockAsync(expiresAt, cancellationToken);
    }

    private async Task UpdateChallengeClockAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                OnPropertyChanged(nameof(ChallengeExpiryText));
                if (_timeProvider.GetUtcNow() >= expiresAt)
                {
                    // A lost Complete response may already have consumed the challenge.
                    // Keep its reference until the server resolves that completion.
                    if (_retryOperation != RetryOperation.Complete) MarkChallengeTerminal(AuthChallengeState.Expired);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void StartResendClock()
    {
        StopResendClock();
        if (_timeProvider.GetUtcNow() >= _resendAvailableAt)
        {
            return;
        }

        _resendClockCancellation = new CancellationTokenSource();
        _ = UpdateResendClockAsync(_resendClockCancellation.Token);
    }

    private async Task UpdateResendClockAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _timeProvider.GetUtcNow() < _resendAvailableAt)
            {
                OnPropertyChanged(nameof(CanResend));
                OnPropertyChanged(nameof(CanShowResend));
                OnPropertyChanged(nameof(ResendCooldownText));
                OnPropertyChanged(nameof(CanRetry));
                LoginCommand.NotifyCanExecuteChanged();
                ResendCodeCommand.NotifyCanExecuteChanged();
                RetryCommand.NotifyCanExecuteChanged();
                await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken);
            }

            OnPropertyChanged(nameof(CanResend));
            OnPropertyChanged(nameof(CanShowResend));
            OnPropertyChanged(nameof(ResendCooldownText));
            OnPropertyChanged(nameof(CanRetry));
            ResendCodeCommand.NotifyCanExecuteChanged();
            RetryCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void StopChallengeClock()
    {
        _challengeClockCancellation?.Cancel();
        _challengeClockCancellation?.Dispose();
        _challengeClockCancellation = null;
    }

    private void StopResendClock()
    {
        _resendClockCancellation?.Cancel();
        _resendClockCancellation?.Dispose();
        _resendClockCancellation = null;
    }

    private void ApplyChallenge(SignInChallenge challenge, bool restartClock, bool clearCode = true)
    {
        _challenge = challenge;
        _challengeUsesRecoveryCode = IsRecoveryCode;
        _isApplyingChallenge = true;
        IsChallengeTerminal = challenge.State is AuthChallengeState.Expired or AuthChallengeState.Rejected or AuthChallengeState.Cancelled;
        RequiresTwoFactor = challenge.NeedsCode;
        TelegramUrl = string.IsNullOrWhiteSpace(challenge.TelegramUrl) ? null : challenge.TelegramUrl;
        StatusMessage = challenge.State == AuthChallengeState.Waiting
            ? IsRecoveryCode
                ? _localization.GetString("Login_RecoveryCodeHint")
                : challenge.Factor == OtpTypeId.Telegram
                ? _localization.GetString("Login_ChallengeWaiting")
                : challenge.NeedsCode
                    ? _localization.GetString("Login_ChallengeCodeSent")
                    : null
            : null;
        if (clearCode)
        {
            Code = string.Empty;
        }
        UpdateFactorOptions(challenge);
        SelectedFactor = challenge.Factor;
        _isApplyingChallenge = false;
        Step = LoginStep.Challenge;
        OnPropertyChanged(nameof(HasTelegramUrl));
        OnPropertyChanged(nameof(CanUseRecoveryCode));
        OnPropertyChanged(nameof(CanSelectFactor));
        OnPropertyChanged(nameof(CanShowResend));
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(ChallengeExpiryText));
        OnPropertyChanged(nameof(CodePlaceholder));
        OnPropertyChanged(nameof(CodeHint));
        if (restartClock)
        {
            StartChallengeClock();
        }
    }

    private void UpdateFactorOptions(SignInChallenge challenge)
    {
        IEnumerable<OtpTypeId> factors = _loginMode == AuthLoginMode.TelegramLogin
            ? [OtpTypeId.Telegram]
            : challenge.AvailableFactors;
        var distinctFactors = factors.Distinct().ToArray();
        if (AvailableFactors.Select(option => option.Factor).SequenceEqual(distinctFactors)) return;
        AvailableFactors.Clear();
        foreach (var factor in distinctFactors)
        {
            AvailableFactors.Add(new LoginFactorOption(factor, _localization.GetString(factor switch
            {
                OtpTypeId.Authenticator => "Login_FactorAuthenticator",
                OtpTypeId.Email => "Login_FactorEmail",
                OtpTypeId.Telegram => "Login_FactorTelegram",
                _ => "Login_FactorUnknown"
            })));
        }
    }

    private void MarkChallengeTerminal(AuthChallengeState state)
    {
        IsChallengeTerminal = true;
        _challengePollingCancellation?.Cancel();
        StopChallengeClock();
        StopResendClock();
        SetRetryOperation(RetryOperation.Begin);
        StatusMessage = _localization.GetString(state switch
        {
            AuthChallengeState.Expired => "Login_ChallengeExpired",
            AuthChallengeState.Rejected => "Login_ChallengeRejected",
            _ => "Login_ChallengeCancelled"
        });
        OnPropertyChanged(nameof(CanSelectFactor));
        OnPropertyChanged(nameof(CanShowResend));
        OnPropertyChanged(nameof(CanResend));
        LoginCommand.NotifyCanExecuteChanged();
    }

    private void SetResendCooldown(DateTimeOffset availableAt)
    {
        _resendAvailableAt = availableAt;
        StartResendClock();
        OnPropertyChanged(nameof(CanShowResend));
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(ResendCooldownText));
        OnPropertyChanged(nameof(CanRetry));
        ResendCodeCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }

    private void ResetResendCooldown()
    {
        StopResendClock();
        _resendAvailableAt = _timeProvider.GetUtcNow();
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(ResendCooldownText));
        OnPropertyChanged(nameof(CanRetry));
        ResendCodeCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }

    private void SetRetryOperation(RetryOperation operation)
    {
        _retryOperation = operation;
        OnPropertyChanged(nameof(CanRetry));
        RetryCommand.NotifyCanExecuteChanged();
    }

    private bool IsCurrentAttempt(AuthenticationAttempt attempt) =>
        ReferenceEquals(_attempt, attempt) && _authenticationService.IsCurrentAuthenticationAttempt(attempt);

    private bool CanLogin() => !IsAuthenticating && Step switch
    {
        LoginStep.Identifier => !string.IsNullOrWhiteSpace(LoginOrEmail),
        LoginStep.Password => !string.IsNullOrWhiteSpace(Password),
        LoginStep.Challenge => _challenge is not null && !IsChallengeTerminal && RequiresTwoFactor && !string.IsNullOrWhiteSpace(Code),
        _ => false
    };

    private RetryOperation RetryOperationForCurrentStep() => Step switch
    {
        LoginStep.Identifier => RetryOperation.Resolve,
        LoginStep.Password => RetryOperation.Begin,
        LoginStep.Challenge => _challenge is null ? RetryOperation.Begin : RetryOperation.Complete,
        _ => RetryOperation.None
    };

    private async Task CancelCurrentAttemptAsync(bool invalidateUi = true)
    {
        if (invalidateUi)
        {
            _flowGeneration++;
            IsAuthenticating = false;
            SetRetryOperation(RetryOperation.None);
        }
        var attempt = _attempt;
        _attempt = null;
        _flowCancellation?.Cancel();
        _flowCancellation?.Dispose();
        _flowCancellation = null;
        StopChallengeClock();
        StopResendClock();
        // Invalidate the service attempt before waiting for a polling Complete/Save.
        var retirement = attempt is null ? Task.CompletedTask
            : _authenticationService.CancelAuthenticationAttemptAsync(attempt);
        await StopChallengePollingAsync();
        await retirement;
    }

    private void StopFastAuthUi()
    {
        Interlocked.Increment(ref _fastAuthGeneration);
        _fastAuthCancellation?.Cancel();
        _fastAuthCancellation = null;
        IsFastAuthLoading = false;
        FastAuthQrCode = null;
        FastAuthStatusMessage = null;
        FastAuthCountdown = string.Empty;
    }

    private void RestartFastAuthForCurrentAttempt()
    {
        StopFastAuthUi();
        if (_attempt is null || !IsIdentifierStep) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _flowCancellation?.Token ?? CancellationToken.None);
        _fastAuthCancellation = cancellation;
        var generation = Interlocked.Increment(ref _fastAuthGeneration);
        _ = RunFastAuthAsync(_attempt, cancellation, generation);
    }

    private void NotifyStepPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsIdentifierStep));
        OnPropertyChanged(nameof(IsPasswordStep));
        OnPropertyChanged(nameof(IsChallengeStep));
        OnPropertyChanged(nameof(CanSubmitChallenge));
        OnPropertyChanged(nameof(IsResolvedIdentifier));
        OnPropertyChanged(nameof(SubmitButtonText));
        OnPropertyChanged(nameof(CanLogin));
        OnPropertyChanged(nameof(CanRetry));
        LoginCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }

    private async Task UpdateFastAuthCountdownAsync(FastAuthSession session, int generation, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsCurrentFastAuthGeneration(generation))
        {
            var remaining = session.QrCode.ExpiresAt - _timeProvider.GetUtcNow();
            FastAuthCountdown = remaining > TimeSpan.Zero
                ? string.Format(_localization.GetString("FastAuth_ExpiresIn"), $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}")
                : string.Empty;
            await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken);
        }
    }

    private async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool IsCurrentFastAuthGeneration(int generation) => generation == Volatile.Read(ref _fastAuthGeneration);

    private static string? ValidateBase64Png(string base64Png) =>
        Convert.TryFromBase64String(base64Png, new byte[((base64Png.Length + 3) / 4) * 3], out var written) && written > 0
            ? base64Png
            : null;

    partial void OnStepChanged(LoginStep value)
    {
        NotifyStepPropertiesChanged();
        OnPropertyChanged(nameof(CanShowResend));
        OnPropertyChanged(nameof(CanResend));
    }

    partial void OnIsAuthenticatingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectFactor));
        OnPropertyChanged(nameof(CanChangeRecoveryCode));
        LoginCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanShowResend));
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(CanRetry));
        ResendCodeCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }

    private void OnIsRecoveryCodeChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectFactor));
        OnPropertyChanged(nameof(CanShowResend));
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(CodePlaceholder));
        OnPropertyChanged(nameof(CodeHint));
        if (!_isApplyingChallenge && IsChallengeStep && _challenge is not null && !IsAuthenticating)
        {
            _ = RestartChallengeAsync();
        }
    }

    private void OnSelectedFactorChanged(OtpTypeId value)
    {
        OnPropertyChanged(nameof(CanSelectFactor));
        OnPropertyChanged(nameof(CanShowResend));
        OnPropertyChanged(nameof(CanResend));
        if (!_isApplyingChallenge && IsChallengeStep && CanSelectFactor && _challenge is not null && !IsAuthenticating)
        {
            _ = RestartChallengeAsync();
        }
    }

    partial void OnTelegramUrlChanged(string? value) => OnPropertyChanged(nameof(HasTelegramUrl));

    partial void OnLoginOrEmailChanged(string value)
    {
        LoginCommand.NotifyCanExecuteChanged();
    }

    partial void OnPasswordChanged(string value) => LoginCommand.NotifyCanExecuteChanged();

    partial void OnCodeChanged(string value)
    {
        OnPropertyChanged(nameof(OtpCode));
        LoginCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsChallengeTerminalChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChangeRecoveryCode));
        OnPropertyChanged(nameof(CanSelectFactor));
        OnPropertyChanged(nameof(CanShowResend));
        OnPropertyChanged(nameof(CanResend));
        ResendCodeCommand.NotifyCanExecuteChanged();
    }

    partial void OnRequiresTwoFactorChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSubmitChallenge));
        LoginCommand.NotifyCanExecuteChanged();
    }

    private bool _challengeUsesRecoveryCode;

    private enum RetryOperation
    {
        None,
        Resolve,
        Begin,
        Complete,
        Resend,
        RestartChallenge
    }
}

public sealed record LoginFactorOption(OtpTypeId Factor, string DisplayName);
