using BarkFluff.ClientV2.WPF.Services;
using BarkFluff.ClientV2.WPF.Infrastructure.Localization;

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

    public async Task LoadFastAuthAsync()
    {
        if (IsFastAuthLoading)
        {
            return;
        }

        IsFastAuthLoading = true;
        try
        {
            var code = await _authenticationService.CreateFastAuthQrCodeAsync();
            FastAuthQrCode = code is null ? null : CreateImage(code.Base64Png);
        }
        finally
        {
            IsFastAuthLoading = false;
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

            StatusMessage = _localization.GetString("Login_Success");
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
