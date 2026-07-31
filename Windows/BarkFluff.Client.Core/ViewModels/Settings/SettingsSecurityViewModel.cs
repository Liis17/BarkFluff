using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsSecurityViewModel : ObservableObject
{
    private const int ConfirmationCodeLength = 6;
    private const int MinimumPasswordLength = 6;

    private readonly ISecuritySettingsService _service;
    private readonly ILocalizationService _localization;

    /// <summary>
    /// Переключатели отражают состояние сервера, поэтому программная синхронизация с ним не должна
    /// снова запускать сценарий подключения или отключения.
    /// </summary>
    private bool _isSyncingSwitches;

    private TwoFactorMethod _pendingMethod;
    private string _resetId = string.Empty;
    private bool _isAuthenticatorEnabled;
    private bool _isEmailEnabled;

    public SettingsSecurityViewModel(ISecuritySettingsService service, ILocalizationService localization)
    {
        _service = service;
        _localization = localization;
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsIdle),
        nameof(IsPasswordRequest),
        nameof(IsPasswordCode),
        nameof(IsPasswordNew),
        nameof(IsTwoFactorSetup),
        nameof(IsTwoFactorDisable),
        nameof(IsCodeRequested))]
    private SecurityFlow _flow;

    [ObservableProperty]
    private string _confirmationCode = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _repeatPassword = string.Empty;

    /// <summary>QR остаётся строкой base64: картинку собирает конвертер представления.</summary>
    [ObservableProperty]
    private string? _qrCode;

    [ObservableProperty]
    private string _manualCode = string.Empty;

    /// <summary>Подключается почта, а не приложение: QR-кода не будет, код придёт письмом.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthenticatorSetup))]
    private bool _isEmailSetup;

    /// <summary>
    /// Обратное к <see cref="IsEmailSetup"/>: <c>BoolToVisibilityConverter</c> не умеет
    /// инвертировать, а расширять общий конвертер ради одной подсказки незачем.
    /// </summary>
    public bool IsAuthenticatorSetup => !IsEmailSetup;

    public bool IsIdle => Flow == SecurityFlow.None;

    public bool IsPasswordRequest => Flow == SecurityFlow.PasswordRequest;

    public bool IsPasswordCode => Flow == SecurityFlow.PasswordCode;

    public bool IsPasswordNew => Flow == SecurityFlow.PasswordNew;

    public bool IsTwoFactorSetup => Flow == SecurityFlow.TwoFactorSetup;

    public bool IsTwoFactorDisable => Flow == SecurityFlow.TwoFactorDisable;

    /// <summary>Шаги, на которых пользователь вводит шестизначный код.</summary>
    public bool IsCodeRequested => Flow is SecurityFlow.PasswordCode or SecurityFlow.TwoFactorSetup or SecurityFlow.TwoFactorDisable;

    public bool IsAuthenticatorEnabled
    {
        get => _isAuthenticatorEnabled;
        set
        {
            if (!SetProperty(ref _isAuthenticatorEnabled, value) || _isSyncingSwitches)
            {
                return;
            }

            _ = BeginTwoFactorChangeAsync(TwoFactorMethod.Authenticator, value);
        }
    }

    public bool IsEmailEnabled
    {
        get => _isEmailEnabled;
        set
        {
            if (!SetProperty(ref _isEmailEnabled, value) || _isSyncingSwitches)
            {
                return;
            }

            _ = BeginTwoFactorChangeAsync(TwoFactorMethod.Email, value);
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        var (errorKey, authenticator, email) = await _service.GetStatusAsync(cancellationToken);
        IsBusy = false;

        if (errorKey is not null)
        {
            ShowError(errorKey);
            return;
        }

        SyncSwitches(authenticator, email);
    }

    [RelayCommand]
    private void BeginPasswordChange()
    {
        ResetFlow();
        ClearMessages();
        Flow = SecurityFlow.PasswordRequest;
    }

    [RelayCommand]
    private async Task RequestPasswordCodeAsync()
    {
        IsBusy = true;
        var (errorKey, resetId) = await _service.RequestPasswordCodeAsync();
        IsBusy = false;

        if (errorKey is not null)
        {
            ShowError(errorKey);
            return;
        }

        _resetId = resetId;
        ConfirmationCode = string.Empty;
        ErrorMessage = null;
        Flow = SecurityFlow.PasswordCode;
    }

    [RelayCommand]
    private async Task ConfirmPasswordCodeAsync()
    {
        if (!IsConfirmationCodeValid())
        {
            ShowError("Error_ConfirmationCodeFormat");
            return;
        }

        IsBusy = true;
        var errorKey = await _service.ConfirmPasswordCodeAsync(_resetId, ConfirmationCode);
        IsBusy = false;

        if (errorKey is not null)
        {
            ShowError(errorKey);
            return;
        }

        NewPassword = string.Empty;
        RepeatPassword = string.Empty;
        ErrorMessage = null;
        Flow = SecurityFlow.PasswordNew;
    }

    [RelayCommand]
    private async Task SavePasswordAsync()
    {
        if (NewPassword.Length < MinimumPasswordLength)
        {
            ShowError("Error_PasswordTooShort");
            return;
        }

        if (!string.Equals(NewPassword, RepeatPassword, StringComparison.Ordinal))
        {
            ShowError("Error_PasswordMismatch");
            return;
        }

        IsBusy = true;
        var errorKey = await _service.SetPasswordAsync(NewPassword);
        IsBusy = false;

        if (errorKey is not null)
        {
            ShowError(errorKey);
            return;
        }

        ResetFlow();
        ClearMessages();
        StatusMessage = _localization.GetString("Settings_Security_PasswordChanged");
    }

    [RelayCommand]
    private async Task ConfirmTwoFactorAsync()
    {
        if (!IsConfirmationCodeValid())
        {
            ShowError("Error_ConfirmationCodeFormat");
            return;
        }

        IsBusy = true;
        var errorKey = Flow == SecurityFlow.TwoFactorSetup
            ? await _service.ConfirmTwoFactorAsync(ConfirmationCode)
            : await _service.DisableTwoFactorAsync(_pendingMethod, ConfirmationCode);
        IsBusy = false;

        if (errorKey is not null)
        {
            ShowError(errorKey);
            return;
        }

        ResetFlow();
        ClearMessages();
        await LoadAsync();
    }

    /// <summary>
    /// Отмена возвращает переключатель к состоянию сервера: до подтверждения ничего не изменилось.
    /// </summary>
    [RelayCommand]
    private void CancelFlow()
    {
        switch (Flow)
        {
            case SecurityFlow.TwoFactorSetup:
                RevertSwitch(_pendingMethod, false);
                break;
            case SecurityFlow.TwoFactorDisable:
                RevertSwitch(_pendingMethod, true);
                break;
        }

        ResetFlow();
        ClearMessages();
    }

    private async Task BeginTwoFactorChangeAsync(TwoFactorMethod method, bool enable)
    {
        _pendingMethod = method;
        ErrorMessage = null;
        StatusMessage = null;
        ConfirmationCode = string.Empty;

        if (!enable)
        {
            // Отключение почты кода не требует — так же, как в Android.
            if (method == TwoFactorMethod.Email)
            {
                await DisableWithoutCodeAsync(method);
                return;
            }

            Flow = SecurityFlow.TwoFactorDisable;
            return;
        }

        IsEmailSetup = method == TwoFactorMethod.Email;
        QrCode = null;
        ManualCode = string.Empty;
        Flow = SecurityFlow.TwoFactorSetup;

        IsBusy = true;
        var (errorKey, qrBase64, manualCode) = await _service.BeginTwoFactorSetupAsync(method);
        IsBusy = false;

        if (errorKey is not null)
        {
            ShowError(errorKey);
            RevertSwitch(method, false);
            ResetFlow();
            return;
        }

        QrCode = qrBase64;
        ManualCode = manualCode;
    }

    private async Task DisableWithoutCodeAsync(TwoFactorMethod method)
    {
        IsBusy = true;
        var errorKey = await _service.DisableTwoFactorAsync(method, string.Empty);
        IsBusy = false;

        if (errorKey is not null)
        {
            ShowError(errorKey);
            RevertSwitch(method, true);
        }
    }

    private void RevertSwitch(TwoFactorMethod method, bool value)
    {
        _isSyncingSwitches = true;
        if (method == TwoFactorMethod.Authenticator)
        {
            IsAuthenticatorEnabled = value;
        }
        else
        {
            IsEmailEnabled = value;
        }

        _isSyncingSwitches = false;
    }

    private void SyncSwitches(bool authenticator, bool email)
    {
        _isSyncingSwitches = true;
        IsAuthenticatorEnabled = authenticator;
        IsEmailEnabled = email;
        _isSyncingSwitches = false;
    }

    /// <summary>
    /// Сбрасывает шаг и введённые данные, но не сообщения: после неудачи ошибка должна пережить
    /// возврат к исходному состоянию, иначе экран промолчит о причине.
    /// </summary>
    private void ResetFlow()
    {
        Flow = SecurityFlow.None;
        ConfirmationCode = string.Empty;
        NewPassword = string.Empty;
        RepeatPassword = string.Empty;
        QrCode = null;
        ManualCode = string.Empty;
        IsEmailSetup = false;
        _resetId = string.Empty;
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        StatusMessage = null;
    }

    private bool IsConfirmationCodeValid() =>
        ConfirmationCode.Length == ConfirmationCodeLength && ConfirmationCode.All(char.IsAsciiDigit);

    private void ShowError(string errorKey)
    {
        StatusMessage = null;
        ErrorMessage = _localization.GetString(errorKey);
    }
}
