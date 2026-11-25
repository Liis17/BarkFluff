using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsNotificationsViewModel(
    IDeviceSettingsService service, ILocalizationService localization, MessageNotificationService notifications) : ObservableObject
{
    private Func<bool>? _isCurrent;
    private CancellationToken _lifetime;
    private Task? _load;
    private bool _hasLoadedDevice;
    private bool _saving;

    internal Task PendingLoad => _load ?? Task.CompletedTask;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _canChange;
    [ObservableProperty] private string? _errorMessage;

    internal void StartSession(Func<bool> isCurrent, CancellationToken lifetime)
    {
        _isCurrent = isCurrent;
        _lifetime = lifetime;
        _ = LoadAsync();
    }

    internal void EndSession()
    {
        _isCurrent = null;
        _load = null;
        _hasLoadedDevice = false;
        _saving = false;
        CanChange = false;
        IsEnabled = false;
        ErrorMessage = null;
    }

    internal void RetryInitialLoad()
    {
        if (!_hasLoadedDevice)
        {
            _ = LoadAsync();
        }
    }

    public Task LoadAsync()
    {
        if (_isCurrent is not { } current || !current())
        {
            return Task.CompletedTask;
        }

        return _load is { IsCompleted: false } ? _load :
            _load = LoadAsync(current, _lifetime);
    }

    private async Task LoadAsync(Func<bool> current, CancellationToken lifetime)
    {
        // Не читаем снимок устройства во время сохранения переключателя.
        if (_saving)
        {
            return;
        }

        CanChange = false;
        ErrorMessage = null;
        try
        {
            var (error, device) = await service.GetCurrentDeviceAsync(lifetime).WaitAsync(lifetime);
            if (!current())
            {
                return;
            }

            if (error is not null || device is null)
            {
                ErrorMessage = localization.GetString(error ?? "Error_SettingsLoadFailed");
                return;
            }

            _hasLoadedDevice = true;
            IsEnabled = device.NotificationsEnabled;
            if (!IsEnabled)
            {
                notifications.Clear();
            }
            CanChange = true;
        }
        catch (Exception)
        {
            if (current())
            {
                ErrorMessage = localization.GetString("Error_SettingsLoadFailed");
            }
        }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_isCurrent is not { } current || !current() || !CanChange || enabled == IsEnabled)
        {
            return;
        }

        var lifetime = _lifetime;
        _saving = true;
        CanChange = false;
        ErrorMessage = null;
        IsEnabled = enabled;
        if (!enabled)
        {
            notifications.Clear();
        }

        string? error;
        try
        {
            error = await service.SetNotificationsEnabledAsync(enabled, lifetime).WaitAsync(lifetime);
        }
        catch (Exception)
        {
            error = "Error_SettingsSaveFailed";
        }

        if (!current())
        {
            return;
        }

        if (error is not null)
        {
            IsEnabled = !enabled;
            if (!IsEnabled)
            {
                notifications.Clear();
            }
            ErrorMessage = localization.GetString(error);
        }

        _saving = false;
        CanChange = true;
    }
}
