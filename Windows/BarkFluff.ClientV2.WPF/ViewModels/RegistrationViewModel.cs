using BarkFluff.ClientV2.WPF.Infrastructure.Localization;
using BarkFluff.ClientV2.WPF.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.ClientV2.WPF.ViewModels;

public sealed partial class RegistrationViewModel : ObservableObject
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IOnboardingNavigationService _navigation;
    private readonly ILocalizationService _localization;
    private string? _codeId;

    public RegistrationViewModel(
        IAuthenticationService authenticationService,
        IOnboardingNavigationService navigation,
        ILocalizationService localization)
    {
        _authenticationService = authenticationService;
        _navigation = navigation;
        _localization = localization;
    }

    [ObservableProperty]
    private string _firstName = string.Empty;

    [ObservableProperty]
    private string _lastName = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _confirmationCode = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _passwordConfirmation = string.Empty;

    [ObservableProperty]
    private int _step;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsProfileStep => Step == 0;

    public bool IsConfirmationStep => Step == 1;

    public bool IsPasswordStep => Step == 2;

    [RelayCommand]
    private async Task CreateAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(FirstName)
            || string.IsNullOrWhiteSpace(Username)
            || string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = _localization.GetString("Error_RegistrationFieldsRequired");
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await _authenticationService.StartRegistrationAsync(FirstName, LastName, Username, Email);
            if (!result.IsSuccess)
            {
                ErrorMessage = _localization.GetString(result.ErrorResourceKey!);
                return;
            }

            _codeId = result.CodeId;
            Step = 1;
            StatusMessage = _localization.GetString("Registration_CodeSent");
        }
        catch (Exception)
        {
            ErrorMessage = _localization.GetString("Error_RegistrationFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(_codeId) || string.IsNullOrWhiteSpace(ConfirmationCode))
        {
            ErrorMessage = _localization.GetString("Error_RegistrationCodeInvalid");
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await _authenticationService.ConfirmRegistrationAsync(_codeId, ConfirmationCode);
            if (!result.IsSuccess)
            {
                ErrorMessage = _localization.GetString(result.ErrorResourceKey!);
                return;
            }

            Step = 2;
            StatusMessage = _localization.GetString("Registration_EmailConfirmed");
        }
        catch (Exception)
        {
            ErrorMessage = _localization.GetString("Error_RegistrationCodeInvalid");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(Password) || Password != PasswordConfirmation)
        {
            ErrorMessage = _localization.GetString("Error_PasswordMismatch");
            return;
        }

        if (string.IsNullOrWhiteSpace(_codeId))
        {
            ErrorMessage = _localization.GetString("Error_RegistrationFailed");
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var result = await _authenticationService.SetPasswordAsync(Password);
            if (!result.IsSuccess)
            {
                ErrorMessage = _localization.GetString(result.ErrorResourceKey!);
                return;
            }

            StatusMessage = _localization.GetString("Registration_Success");
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
        _codeId = null;
        FirstName = string.Empty;
        LastName = string.Empty;
        Username = string.Empty;
        Email = string.Empty;
        ConfirmationCode = string.Empty;
        Password = string.Empty;
        PasswordConfirmation = string.Empty;
        Step = 0;
        ErrorMessage = null;
        StatusMessage = null;
    }

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsProfileStep));
        OnPropertyChanged(nameof(IsConfirmationStep));
        OnPropertyChanged(nameof(IsPasswordStep));
    }
}
