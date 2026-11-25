using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsChatFoldersViewModel(
    IChatFoldersState state, MessengerViewModel messenger, ILocalizationService localization) : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasFolders;
    [ObservableProperty] private bool _canEdit = true;
    private string? _saveError;
    private CancellationToken _lifetime;
    private bool _active;

    public ObservableCollection<SettingsChatFolderItem> Folders { get; } = [];
    public bool IsEmpty => !HasFolders;
    public CancellationToken Lifetime => state.Lifetime;

    partial void OnHasFoldersChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    public Task ActivateAsync()
    {
        _active = true;
        state.Changed += OnStateChanged;
        UpdateView();
        return state.RefreshAsync();
    }

    public void Deactivate()
    {
        _active = false;
        state.Changed -= OnStateChanged;
        _saveError = null;
        while (Folders.Count > 0)
        {
            Folders.RemoveAt(Folders.Count - 1);
        }
        HasFolders = false;
        ErrorMessage = null;
    }

    private void OnStateChanged(object? sender, EventArgs args) => UpdateView();

    private void UpdateView()
    {
        if (_lifetime != state.Lifetime)
        {
            _lifetime = state.Lifetime;
            _saveError = null;
        }

        var expected = state.Folders.Select(folder => new SettingsChatFolderItem(
            folder.FolderId, folder.FolderName, folder.FolderIcon)).ToArray();
        for (var index = Folders.Count - 1; index >= 0; index--)
        {
            if (!expected.Contains(Folders[index]))
            {
                Folders.RemoveAt(index);
            }
        }
        for (var index = 0; index < expected.Length; index++)
        {
            if (index < Folders.Count && Folders[index] == expected[index])
            {
                continue;
            }
            var previous = Folders.IndexOf(expected[index]);
            if (previous >= 0)
            {
                var existing = Folders[previous];
                Folders.RemoveAt(previous);
                Folders.Insert(index, existing);
            }
            else
            {
                Folders.Insert(index, expected[index]);
            }
        }

        HasFolders = Folders.Count > 0;
        IsBusy = state.IsBusy;
        CanEdit = !state.IsSaving && !state.Lifetime.IsCancellationRequested;
        ErrorMessage = (_saveError ?? state.LoadError) is { } error ? localization.GetString(error) : null;
    }

    public Task CreateAsync(string name, string icon) =>
        RunAsync(() => state.SaveAsync(async (service, token) => (await service.CreateChatFolderAsync(name, icon, token)).ErrorKey));

    public Task UpdateAsync(SettingsChatFolderItem item, string name, string icon) =>
        RunAsync(() => state.SaveAsync(async (service, token) => (await service.UpdateChatFolderAsync(item.Id, name, icon, token)).ErrorKey));

    public Task DeleteAsync(SettingsChatFolderItem item) =>
        RunAsync(() => state.SaveAsync((service, token) => service.DeleteChatFolderAsync(item.Id, token)));

    public Task MoveAsync(SettingsChatFolderItem item, int offset)
    {
        var oldIndex = Folders.IndexOf(item);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Folders.Count)
        {
            return Task.CompletedTask;
        }

        var order = Folders.Select(folder => folder.Id).ToList();
        order.RemoveAt(oldIndex);
        order.Insert(newIndex, item.Id);
        return RunAsync(() => state.SaveAsync((service, token) => service.ReorderChatFoldersAsync(order
            .Select((id, index) => KeyValuePair.Create(id, index)).ToDictionary(), token)));
    }

    public ChatFolderSelection? CreateSelection(SettingsChatFolderItem item)
    {
        var folder = state.Folders.FirstOrDefault(folder => folder.FolderId == item.Id);
        return folder is null ? null : new ChatFolderSelection(folder, messenger.Chats, state.Lifetime);
    }

    public Task ApplySelectionAsync(ChatFolderSelection selection)
    {
        var changes = selection.Changes();
        return selection.Lifetime != state.Lifetime || changes.Count == 0
            ? Task.CompletedTask
            : RunAsync(() => state.ApplySelectionAsync(selection.FolderId, changes));
    }

    private async Task RunAsync(Func<Task<string?>> action)
    {
        var lifetime = state.Lifetime;
        _saveError = null;
        var error = await action();
        if (_active && lifetime == state.Lifetime && !lifetime.IsCancellationRequested)
        {
            _saveError = error;
            UpdateView();
        }
    }
}

public sealed record SettingsChatFolderItem(string Id, string Name, string Icon);
