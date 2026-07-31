using BarkFluff.Client.Core.Models;
using BarkFluff.Proto.Users;

namespace BarkFluff.Client.Core.Services;

public interface IUserPreferencesService
{
    Task<(string? ErrorKey, PrivacySettings? Settings)> GetPrivacySettingsAsync(CancellationToken cancellationToken = default);
    Task<string?> UpdatePrivacySettingsAsync(PrivacySettings settings, CancellationToken cancellationToken = default);
    Task<(string? ErrorKey, IReadOnlyList<ChatFolderData>? Folders)> GetChatFoldersAsync(CancellationToken cancellationToken = default) => Task.FromResult<(string?, IReadOnlyList<ChatFolderData>?)>(("Error_SettingsLoadFailed", null));
    Task<(string? ErrorKey, ChatFolderData? Folder)> CreateChatFolderAsync(string name, string icon, CancellationToken cancellationToken = default) => Task.FromResult<(string?, ChatFolderData?)>(("Error_SettingsSaveFailed", null));
    Task<(string? ErrorKey, ChatFolderData? Folder)> UpdateChatFolderAsync(string folderId, string name, string icon, CancellationToken cancellationToken = default) => Task.FromResult<(string?, ChatFolderData?)>(("Error_SettingsSaveFailed", null));
    Task<string?> DeleteChatFolderAsync(string folderId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Error_SettingsSaveFailed");
    Task<string?> ReorderChatFoldersAsync(IReadOnlyDictionary<string, int> orders, CancellationToken cancellationToken = default) => Task.FromResult<string?>("Error_SettingsSaveFailed");
    Task<(string? ErrorKey, UserStorageInfo? Storage)> GetUserStorageInfoAsync(CancellationToken cancellationToken = default);
}
