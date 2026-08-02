using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsAccountViewModel : ObservableObject
{
    private readonly IAccountSettingsService _service;
    private readonly ILocalizationService _localization;

    public SettingsAccountViewModel(
        IAccountSettingsService service,
        ILocalizationService localization)
    {
        _service = service;
        _localization = localization;
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _firstName = string.Empty;

    [ObservableProperty]
    private string _lastName = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _bio = string.Empty;

    [ObservableProperty]
    private string _avatarUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLogoutIdle))]
    private bool _isLogoutConfirmVisible;

    /// <summary>
    /// Обратное к <see cref="IsLogoutConfirmVisible"/>: <c>BoolToVisibilityConverter</c> не умеет
    /// инвертировать, а кнопка и подтверждение не должны быть на экране одновременно.
    /// </summary>
    public bool IsLogoutIdle => !IsLogoutConfirmVisible;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        var (errorKey, profile) = await _service.GetProfileAsync(cancellationToken);
        IsBusy = false;

        if (errorKey is not null || profile is null)
        {
            ShowError(errorKey ?? "Error_SettingsLoadFailed");
            return;
        }

        ErrorMessage = null;
        FirstName = profile.FirstName;
        LastName = profile.LastName;
        Username = profile.Username;
        Bio = profile.Bio;
        AvatarUrl = profile.AvatarUrl;
    }

    public async Task UploadAvatarAsync(string filePath, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        var errorKey = await _service.UploadAvatarAsync(filePath, cancellationToken);
        IsBusy = false;

        if (errorKey is not null)
        {
            ShowError(errorKey);
            return;
        }

        await LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private Task SaveNameAsync() => RunAsync(() => _service.ChangeNameAsync(FirstName, LastName));

    [RelayCommand]
    private Task SaveUsernameAsync() => RunAsync(() => _service.ChangeUsernameAsync(Username));

    [RelayCommand]
    private Task SaveBioAsync() => RunAsync(() => _service.ChangeBioAsync(Bio));

    [RelayCommand]
    private void RequestLogout() => IsLogoutConfirmVisible = true;

    [RelayCommand]
    private void CancelLogout() => IsLogoutConfirmVisible = false;

    [RelayCommand]
    private async Task ConfirmLogoutAsync()
    {
        IsBusy = true;
        await _service.LogoutAsync();
        IsBusy = false;
        IsLogoutConfirmVisible = false;
    }

    private async Task RunAsync(Func<Task<string?>> operation)
    {
        IsBusy = true;
        var errorKey = await operation();
        IsBusy = false;
        ErrorMessage = errorKey is null ? null : _localization.GetString(errorKey);
    }

    private void ShowError(string errorKey) => ErrorMessage = _localization.GetString(errorKey);
}
