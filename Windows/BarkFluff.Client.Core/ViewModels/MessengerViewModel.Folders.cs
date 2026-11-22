using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly IChatFoldersState? _chatFolders;
    [ObservableProperty] private string? _folderActionError;

    public IChatFoldersState? ChatFolders => _chatFolders;

    public async Task SetChatFolderAsync(ChatItemViewModel chat, string folderId, bool included,
        CancellationToken lifetime)
    {
        var session = _realtimeSession;
        if (_chatFolders is null || !IsCurrent(session) || lifetime != _chatFolders.Lifetime || !Chats.Contains(chat))
        {
            return;
        }

        FolderActionError = null;
        var error = await _chatFolders.SetMembershipAsync(folderId, chat.Id, included);
        if (IsCurrent(session) && lifetime == _chatFolders.Lifetime && error is not null)
        {
            FolderActionError = _localization.GetString(error);
        }
    }
}
