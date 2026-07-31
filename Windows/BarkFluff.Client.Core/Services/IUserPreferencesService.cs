using BarkFluff.Proto.Users;

namespace BarkFluff.Client.Core.Services;

public interface IUserPreferencesService
{
    Task<(string? ErrorKey, PrivacySettings? Settings)> GetPrivacySettingsAsync(CancellationToken cancellationToken = default);
    Task<string?> UpdatePrivacySettingsAsync(PrivacySettings settings, CancellationToken cancellationToken = default);
}
