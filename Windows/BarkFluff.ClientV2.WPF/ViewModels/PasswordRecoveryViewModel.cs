using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.ClientV2.WPF.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.ClientV2.WPF.ViewModels;

public sealed partial class PasswordRecoveryViewModel : ObservableObject
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IOnboardingNavigationService _navigation;
    private readonly ILocalizationService _localization;
    private string? _resetId;

    public PasswordRecoveryViewModel(
        IAuthenticationService authenticationService,
        IOnboardingNavigationService navigation,
        ILocalizationService localization)
    {
        _authenticationService = authenticationService;
        _navigation = navigation;
        _localization = localization;
    }

    [ObservableProperty]
    private string _loginOrEmail = string.Empty;

    [ObservableProperty]
    private string _confirmationCode = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _passwordConfirmation = string.Empty;

    [ObservableProperty]
    private bool _isCodeRequested;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [RelayCommand]
    private async Task RequestCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(LoginOrEmail))
        {
            ErrorMessage = _localization.GetString("Error_RecoveryIdentifierRequired");
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await _authenticationService.StartPasswordResetAsync(LoginOrEmail);
            if (!result.IsSuccess)
            {
                ErrorMessage = _localization.GetString(result.ErrorResourceKey!);
                return;
            }

            _resetId = result.ResetId;
            IsCodeRequested = true;
            StatusMessage = _localization.GetString("PasswordRecovery_CodeSent");
        }
        catch (Exception)
        {
            ErrorMessage = _localization.GetString("Error_PasswordResetFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(_resetId) || string.IsNullOrWhiteSpace(ConfirmationCode))
        {
            ErrorMessage = _localization.GetString("Error_PasswordResetCodeInvalid");
            return;
        }

        if (string.IsNullOrWhiteSpace(Password) || Password != PasswordConfirmation)
        {
            ErrorMessage = _localization.GetString("Error_PasswordMismatch");
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await _authenticationService.CompletePasswordResetAsync(_resetId, ConfirmationCode, Password);
            if (!result.IsSuccess)
            {
                ErrorMessage = _localization.GetString(result.ErrorResourceKey!);
                return;
            }

            StatusMessage = _localization.GetString("PasswordRecovery_Success");
            _resetId = null;
            ConfirmationCode = string.Empty;
            Password = string.Empty;
            PasswordConfirmation = string.Empty;
        }
        catch (Exception)
        {
            ErrorMessage = _localization.GetString("Error_PasswordInvalid");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ShowLogin() => _navigation.ShowLogin();

    public void Reset()
    {
        _resetId = null;
        LoginOrEmail = string.Empty;
        ConfirmationCode = string.Empty;
        Password = string.Empty;
        PasswordConfirmation = string.Empty;
        IsCodeRequested = false;
        ErrorMessage = null;
        StatusMessage = null;
    }
}
