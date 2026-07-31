using BarkFluff.Client.Core.Models;
using BarkFluff.Proto.Users;

namespace BarkFluff.Client.Core.Services;

public interface IUserPreferencesService
{
    Task<(string? ErrorKey, PrivacySettings? Settings)> GetPrivacySettingsAsync(CancellationToken cancellationToken = default);
    Task<string?> UpdatePrivacySettingsAsync(PrivacySettings settings, CancellationToken cancellationToken = default);
    Task<(string? ErrorKey, UserStorageInfo? Storage)> GetUserStorageInfoAsync(CancellationToken cancellationToken = default);
}
