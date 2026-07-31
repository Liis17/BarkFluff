using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
namespace BarkFluff.Client.Core.ViewModels.Settings;
public sealed partial class SettingsNotificationsViewModel(IDeviceSettingsService service, ILocalizationService localization):ObservableObject
{
 [ObservableProperty] private bool _isEnabled=true; [ObservableProperty] private string? _errorMessage;
 public async Task SetEnabledAsync(bool enabled) { IsEnabled=enabled; var error=await service.SetNotificationsEnabledAsync(enabled); if(error is not null){ IsEnabled=!enabled; ErrorMessage=localization.GetString(error); } }
}
