using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;
namespace BarkFluff.Client.Core.Services;
public interface IDeviceSettingsService
{
 Task<(string? ErrorKey, Device? Device)> GetCurrentDeviceAsync(CancellationToken ct = default);
 Task<(string? ErrorKey, List<GetActiveSessionsResponse.Types.Session>? Sessions)> GetDevicesAsync(CancellationToken ct = default);
 Task<string?> RemoveSessionAsync(string deviceId, CancellationToken ct = default);
 Task<string?> SetNotificationsEnabledAsync(bool enabled, CancellationToken ct = default);
}
