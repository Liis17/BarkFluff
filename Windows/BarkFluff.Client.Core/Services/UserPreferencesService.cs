using BarkFluff.Client.Core.Models;
using BarkFluff.Proto.Users;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public sealed class UserPreferencesService(WebApiClient webApi, IClientSession session) : SessionScopedService(webApi, session), IUserPreferencesService
{
    public async Task<(string? ErrorKey, PrivacySettings? Settings)> GetPrivacySettingsAsync(CancellationToken cancellationToken = default)
    {
        var (error, settings) = await WebApi.GetPrivacySettings(Parameters);
        return error.IsSuccess && settings is not null ? (null, settings) : ("Error_SettingsLoadFailed", null);
    }

    public async Task<string?> UpdatePrivacySettingsAsync(PrivacySettings settings, CancellationToken cancellationToken = default) =>
        (await WebApi.UpdatePrivacySettings(settings, Parameters)).IsSuccess ? null : "Error_SettingsSaveFailed";

    public async Task<(string? ErrorKey, IReadOnlyList<ChatFolderData>? Folders)> GetChatFoldersAsync(CancellationToken cancellationToken = default)
    {
        var (error, folders) = await WebApi.GetChatFolders(Parameters);
        return error?.IsSuccess == true && folders is not null ? (null, folders) : ("Error_SettingsLoadFailed", null);
    }

    public async Task<(string? ErrorKey, ChatFolderData? Folder)> CreateChatFolderAsync(string name, string icon, CancellationToken cancellationToken = default)
    {
        var (error, folder) = await WebApi.CreateChatFolder(name, Parameters, icon);
        return error?.IsSuccess == true && folder is not null ? (null, folder) : ("Error_SettingsSaveFailed", null);
    }

    public async Task<(string? ErrorKey, ChatFolderData? Folder)> UpdateChatFolderAsync(string folderId, string name, string icon, CancellationToken cancellationToken = default)
    {
        var (error, folder) = await WebApi.UpdateChatFolder(folderId, Parameters, name, icon);
        return error?.IsSuccess == true && folder is not null ? (null, folder) : ("Error_SettingsSaveFailed", null);
    }

    public async Task<(string? ErrorKey, ChatFolderData? Folder)> SetChatFolderMembershipAsync(
        string folderId, string chatId, bool included, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = Parameters;
        var (error, folder) = await (included
            ? WebApi.AddChatToFolder(folderId, chatId, parameters, cancellationToken)
            : WebApi.RemoveChatFromFolder(folderId, chatId, parameters, cancellationToken)).WaitAsync(cancellationToken);
        return error?.IsSuccess == true && folder is not null ? (null, folder) : ("Error_SettingsSaveFailed", null);
    }

    public async Task<string?> DeleteChatFolderAsync(string folderId, CancellationToken cancellationToken = default) =>
        (await WebApi.DeleteChatFolder(folderId, Parameters))?.IsSuccess == true ? null : "Error_SettingsSaveFailed";

    public async Task<string?> ReorderChatFoldersAsync(IReadOnlyDictionary<string, int> orders, CancellationToken cancellationToken = default) =>
        (await WebApi.ReorderChatFolders(orders.ToDictionary(), Parameters))?.IsSuccess == true ? null : "Error_SettingsSaveFailed";

    public async Task<(string? ErrorKey, UserStorageInfo? Storage)> GetUserStorageInfoAsync(CancellationToken cancellationToken = default)
    {
        var (error, usedBytes, limitBytes, usageByType) = await WebApi.GetUserStorageInfoAsync(Parameters);
        return error.IsSuccess
            ? (null, new UserStorageInfo(usedBytes, limitBytes, usageByType))
            : ("Error_SettingsLoadFailed", null);
    }
}
