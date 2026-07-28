using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.ClientV2.WPF.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BarkFluff.ClientV2.WPF.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IOnboardingNavigationService _navigation;
    private readonly ILocalizationService _localization;

    public LoginViewModel(
        IAuthenticationService authenticationService,
        IOnboardingNavigationService navigation,
        ILocalizationService localization)
    {
        _authenticationService = authenticationService;
        _navigation = navigation;
        _localization = localization;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _loginOrEmail = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool _isAuthenticating;

    [ObservableProperty]
    private bool _requiresTwoFactor;

    [ObservableProperty]
    private bool _isFastAuthLoading;

    [ObservableProperty]
    private ImageSource? _fastAuthQrCode;

    [ObservableProperty]
    private string? _fastAuthStatusMessage;

    [ObservableProperty]
    private string _fastAuthCountdown = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _otpDigit1 = string.Empty;

    [ObservableProperty]
    private string _otpDigit2 = string.Empty;

    [ObservableProperty]
    private string _otpDigit3 = string.Empty;

    [ObservableProperty]
    private string _otpDigit4 = string.Empty;

    [ObservableProperty]
    private string _otpDigit5 = string.Empty;

    [ObservableProperty]
    private string _otpDigit6 = string.Empty;

    public string OtpCode => string.Concat(OtpDigit1, OtpDigit2, OtpDigit3, OtpDigit4, OtpDigit5, OtpDigit6);

    private CancellationTokenSource? _fastAuthCancellation;
    private int _fastAuthGeneration;

    public void StopFastAuth()
    {
        _fastAuthGeneration++;
        _fastAuthCancellation?.Cancel();
        _fastAuthCancellation = null;
        IsFastAuthLoading = false;
    }

    public void ClearSensitiveState()
    {
        Password = string.Empty;
        OtpDigit1 = string.Empty;
        OtpDigit2 = string.Empty;
        OtpDigit3 = string.Empty;
        OtpDigit4 = string.Empty;
        OtpDigit5 = string.Empty;
        OtpDigit6 = string.Empty;
    }

    public Task LoadFastAuthAsync()
    {
        StopFastAuth();
        var cancellation = new CancellationTokenSource();
        _fastAuthCancellation = cancellation;
        return RunFastAuthAsync(cancellation, ++_fastAuthGeneration);
    }

    private async Task RunFastAuthAsync(CancellationTokenSource cancellation, int generation)
    {
        var cancellationToken = cancellation.Token;
        IsFastAuthLoading = true;
        ErrorMessage = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!IsCurrentFastAuthGeneration(generation))
                {
                    return;
                }

                FastAuthQrCode = null;
                FastAuthCountdown = string.Empty;
                FastAuthStatusMessage = _localization.GetString("FastAuth_Creating");
                var session = await _authenticationService.CreateFastAuthSessionAsync(cancellationToken);
                if (session is null)
                {
                    if (IsCurrentFastAuthGeneration(generation))
                    {
                        ErrorMessage = _localization.GetString("Error_FastAuthUnavailable");
                    }
                    return;
                }

                FastAuthQrCode = CreateImage(session.QrCode.Base64Png);
                if (FastAuthQrCode is null)
                {
                    if (IsCurrentFastAuthGeneration(generation))
                    {
                        ErrorMessage = _localization.GetString("Error_FastAuthFailed");
                    }
                    return;
                }

                FastAuthStatusMessage = _localization.GetString("FastAuth_WaitingConfirmation");
                using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var countdownTask = UpdateFastAuthCountdownAsync(session, generation, sessionCancellation.Token);
                var terminalStatus = await ObserveFastAuthAsync(session, generation, sessionCancellation.Token);
                sessionCancellation.Cancel();
                await IgnoreCancellationAsync(countdownTask);
                if (terminalStatus == FastAuthUpdateKind.Accepted || terminalStatus == FastAuthUpdateKind.Failed)
                {
                    return;
                }

                await Task.Delay(
                    terminalStatus == FastAuthUpdateKind.Rejected ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(500),
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
                ErrorMessage = _localization.GetString("Error_FastAuthUnavailable");
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
        }
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        IsAuthenticating = true;
        try
        {
            var result = await _authenticationService.LoginAsync(LoginOrEmail, Password, OtpCode);
            RequiresTwoFactor = result.RequiresTwoFactor;
            if (!result.IsSuccess)
            {
                ErrorMessage = _localization.GetString(result.ErrorResourceKey!);
                return;
            }

            StopFastAuth();
            StatusMessage = _localization.GetString("Login_Success");
            _navigation.ShowMessenger();
        }
        catch (Exception)
        {
            ErrorMessage = _localization.GetString("Error_LoginFailed");
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    [RelayCommand]
    private void PasteOtpCode(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).Take(6).ToArray());
        if (digits.Length != 6)
        {
            return;
        }

        OtpDigit1 = digits[0].ToString();
        OtpDigit2 = digits[1].ToString();
        OtpDigit3 = digits[2].ToString();
        OtpDigit4 = digits[3].ToString();
        OtpDigit5 = digits[4].ToString();
        OtpDigit6 = digits[5].ToString();
    }

    [RelayCommand]
    private void ChooseAnotherNode() => _navigation.ShowSelectNode();

    [RelayCommand]
    private void ShowPasswordRecovery() => _navigation.ShowPasswordRecovery();

    [RelayCommand]
    private void ShowRegistration() => _navigation.ShowRegistration();

    private bool CanLogin() => !IsAuthenticating
        && !string.IsNullOrWhiteSpace(LoginOrEmail)
        && !string.IsNullOrWhiteSpace(Password);

    partial void OnOtpDigit1Changed(string value) => TryAutoSubmitTwoFactorCode();
    partial void OnOtpDigit2Changed(string value) => TryAutoSubmitTwoFactorCode();
    partial void OnOtpDigit3Changed(string value) => TryAutoSubmitTwoFactorCode();
    partial void OnOtpDigit4Changed(string value) => TryAutoSubmitTwoFactorCode();
    partial void OnOtpDigit5Changed(string value) => TryAutoSubmitTwoFactorCode();
    partial void OnOtpDigit6Changed(string value) => TryAutoSubmitTwoFactorCode();

    private void TryAutoSubmitTwoFactorCode()
    {
        OnPropertyChanged(nameof(OtpCode));
        if (RequiresTwoFactor && OtpCode.Length == 6 && OtpCode.All(char.IsDigit) && !IsAuthenticating)
        {
            _ = LoginAsync();
        }
    }

    private async Task<FastAuthUpdateKind> ObserveFastAuthAsync(FastAuthSession session, int generation, CancellationToken cancellationToken)
    {
        await foreach (var update in _authenticationService.SubscribeFastAuthAsync(session, cancellationToken))
        {
            if (!IsCurrentFastAuthGeneration(generation))
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
                    _navigation.ShowMessenger();
                    return update.Kind;
                case FastAuthUpdateKind.Rejected:
                    FastAuthStatusMessage = _localization.GetString("FastAuth_Rejected");
                    return update.Kind;
                case FastAuthUpdateKind.Expired:
                    FastAuthStatusMessage = _localization.GetString("FastAuth_Expired");
                    return update.Kind;
                case FastAuthUpdateKind.Failed:
                    ErrorMessage = _localization.GetString(update.ErrorResourceKey ?? "Error_FastAuthFailed");
                    return update.Kind;
            }
        }

        return FastAuthUpdateKind.Failed;
    }

    private async Task UpdateFastAuthCountdownAsync(FastAuthSession session, int generation, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!IsCurrentFastAuthGeneration(generation))
            {
                return;
            }

            var remaining = session.QrCode.ExpiresAt - DateTimeOffset.UtcNow;
            FastAuthCountdown = remaining > TimeSpan.Zero
                ? string.Format(_localization.GetString("FastAuth_ExpiresIn"), $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}")
                : string.Empty;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool IsCurrentFastAuthGeneration(int generation) => generation == _fastAuthGeneration;

    private static ImageSource? CreateImage(string base64Png)
    {
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(base64Png));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
