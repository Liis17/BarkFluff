using System.Collections.ObjectModel;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Users;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly IChatFoldersState? _chatFolders;
    private readonly ChatFolderTabViewModel _allChatsTab;
    private ChatFolderTabViewModel? _activeFolderTab;
    private bool _syncingFolderTabs;
    private bool _syncingVisibleChats;
    private Dictionary<string, HashSet<string>> _folderMembers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _chatsInFolders = new(StringComparer.Ordinal);
    [ObservableProperty] private string? _folderActionError;

    public IChatFoldersState? ChatFolders => _chatFolders;

    public ObservableCollection<ChatFolderTabViewModel> ChatFolderTabs { get; } = [];

    public ChatFolderTabViewModel? ActiveFolderTab
    {
        get => _activeFolderTab ?? _allChatsTab;
        set
        {
            if (_syncingFolderTabs || value is null || value == ActiveFolderTab || !ChatFolderTabs.Contains(value))
                return;

            _activeFolderTab = value;
            OnPropertyChanged();
            ApplyChatFilter();
        }
    }

    // ListView снимает выделение при удалении строки из проекции. Открытый чат живёт отдельно.
    public ChatItemViewModel? SelectedVisibleChat
    {
        get => SelectedChat is { } chat && VisibleChats.Contains(chat) ? chat : null;
        set
        {
            if (!_syncingVisibleChats && value is not null && VisibleChats.Contains(value))
                SelectedChat = value;
        }
    }

    private bool IsInActiveFolder(ChatItemViewModel chat) =>
        ActiveFolderTab?.Id is { } id && _folderMembers.TryGetValue(id, out var members)
            ? members.Contains(chat.Id)
            : !_chatsInFolders.Contains(chat.Id);

    private Action ObserveFolderTabs(RealtimeSession session)
    {
        if (_chatFolders is not { } state)
            return () => { };

        IReadOnlyList<ChatFolderData>? last = null;
        void Changed(object? sender, EventArgs args)
        {
            PostSession(session, _ =>
            {
                var folders = state.Folders;
                if (ReferenceEquals(last, folders))
                    return;

                last = folders;
                RefreshFolderTabs(folders);
            });
        }

        state.Changed += Changed;
        Changed(null, EventArgs.Empty);
        return () => state.Changed -= Changed;
    }

    private void RefreshFolderTabs(IReadOnlyList<ChatFolderData> folders)
    {
        _allChatsTab.Title = _localization.GetString("Messenger_AllChatFolders");
        var existing = ChatFolderTabs.Where(tab => tab.Id is not null).ToDictionary(tab => tab.Id!, StringComparer.Ordinal);
        var next = new List<ChatFolderTabViewModel> { _allChatsTab };
        _folderMembers = new(StringComparer.Ordinal);
        _chatsInFolders.Clear();
        foreach (var folder in folders.OrderBy(folder => folder.SortOrder).ThenBy(folder => folder.FolderId, StringComparer.Ordinal))
        {
            var members = folder.ChatList.ToHashSet(StringComparer.Ordinal);
            _folderMembers.Add(folder.FolderId, members);
            _chatsInFolders.UnionWith(members);
            if (!existing.TryGetValue(folder.FolderId, out var tab))
                tab = new(folder.FolderId, _localization);
            tab.Title = folder.FolderName;
            next.Add(tab);
        }

        var active = ActiveFolderTab;
        _syncingFolderTabs = true;
        try
        {
            SyncList(ChatFolderTabs, next, active);
        }
        finally
        {
            _syncingFolderTabs = false;
        }
        if (active is not null && !next.Contains(active))
            ActiveFolderTab = _allChatsTab;
        else
            ApplyChatFilter();
    }

    private void UpdateFolderUnreadCounts()
    {
        if (_folderMembers.Count == 0)
            return;

        var unreadByChat = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var chat in Chats)
        {
            if (chat.UnreadCount > 0 && _chatsInFolders.Contains(chat.Id))
                unreadByChat[chat.Id] = unreadByChat.GetValueOrDefault(chat.Id) + chat.UnreadCount;
        }

        // Web учитывает непрочитанное и в заглушённых чатах. У вкладки «Все» счётчика нет.
        foreach (var tab in ChatFolderTabs)
        {
            if (tab.Id is { } id)
                tab.UnreadCount = _folderMembers[id].Sum(chatId => unreadByChat.GetValueOrDefault(chatId));
        }
    }

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
