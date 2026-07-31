using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsChatFoldersViewModel(IUserPreferencesService service, ILocalizationService localization) : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasFolders;

    public ObservableCollection<SettingsChatFolderItem> Folders { get; } = [];

    public async Task LoadAsync()
    {
        IsBusy = true;
        var (error, folders) = await service.GetChatFoldersAsync();
        IsBusy = false;
        if (error is not null || folders is null)
        {
            ErrorMessage = localization.GetString(error ?? "Error_SettingsLoadFailed");
            return;
        }

        ErrorMessage = null;
        Folders.Clear();
        foreach (var folder in folders.OrderBy(folder => folder.SortOrder))
        {
            Folders.Add(new SettingsChatFolderItem(folder.FolderId, folder.FolderName, folder.FolderIcon));
        }

        HasFolders = Folders.Count > 0;
    }

    public async Task CreateAsync(string name, string icon)
    {
        var (error, _) = await service.CreateChatFolderAsync(name, icon);
        if (error is not null)
        {
            ErrorMessage = localization.GetString(error);
            return;
        }

        await LoadAsync();
    }

    public async Task UpdateAsync(SettingsChatFolderItem item, string name, string icon)
    {
        var (error, _) = await service.UpdateChatFolderAsync(item.Id, name, icon);
        if (error is not null)
        {
            ErrorMessage = localization.GetString(error);
            return;
        }

        await LoadAsync();
    }

    public async Task DeleteAsync(SettingsChatFolderItem item)
    {
        var error = await service.DeleteChatFolderAsync(item.Id);
        if (error is not null)
        {
            ErrorMessage = localization.GetString(error);
            return;
        }

        await LoadAsync();
    }

    public async Task MoveAsync(SettingsChatFolderItem item, int offset)
    {
        var oldIndex = Folders.IndexOf(item);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Folders.Count)
        {
            return;
        }

        Folders.Move(oldIndex, newIndex);
        var error = await service.ReorderChatFoldersAsync(Folders
            .Select((folder, index) => KeyValuePair.Create(folder.Id, index))
            .ToDictionary());
        if (error is null)
        {
            return;
        }

        ErrorMessage = localization.GetString(error);
        await LoadAsync();
    }
}

public sealed record SettingsChatFolderItem(string Id, string Name, string Icon);
