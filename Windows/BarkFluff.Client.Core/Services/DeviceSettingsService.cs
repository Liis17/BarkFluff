using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;
namespace BarkFluff.Client.Core.Services;
public sealed class DeviceSettingsService(WebApiClient api, IClientSession session) : SessionScopedService(api, session), IDeviceSettingsService
{
 public async Task<(string? ErrorKey, Device? Device)> GetCurrentDeviceAsync(CancellationToken ct = default) { var (e,d)=await WebApi.GetCurrentDevice(Parameters); return e.IsSuccess?(null,d):("Error_SettingsLoadFailed",null); }
 public async Task<(string? ErrorKey, List<GetActiveSessionsResponse.Types.Session>? Sessions)> GetDevicesAsync(CancellationToken ct = default) { var (e,s)=await WebApi.GetDevicesList(Parameters); return e.IsSuccess?(null,s):("Error_SettingsLoadFailed",null); }
 public async Task<string?> RemoveSessionAsync(string id,CancellationToken ct=default)=>(await WebApi.RemoveActiveSession(id,Parameters)).IsSuccess?null:"Error_SettingsSaveFailed";
 public async Task<string?> SetNotificationsEnabledAsync(bool enabled,CancellationToken ct=default)=>(await WebApi.SetNotificationsEnabled(enabled,Parameters)).IsSuccess?null:"Error_SettingsSaveFailed";
}
