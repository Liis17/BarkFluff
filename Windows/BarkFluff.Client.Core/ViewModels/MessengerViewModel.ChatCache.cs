using System.Collections.Specialized;
using System.ComponentModel;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using CommunityToolkit.Mvvm.ComponentModel;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly ChatListCache? _chatCache;
    private readonly IAuthenticationService? _authentication;
    private readonly IOnboardingNavigationService? _navigation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatListReconnectingVisible))]
    private bool _isOffline;

    private static ChatListScope CacheScope(RealtimeSession session) => ChatListScope.Create(session.Node, session.UserId);

    private async Task<IReadOnlyList<ChatFolderData>> LoadChatCacheAsync(RealtimeSession session)
    {
        if (_chatCache is null || session.CacheLoaded)
            return [];

        session.CacheLoaded = true;
        var cached = await _chatCache.LoadAsync(CacheScope(session));
        if (!IsCurrent(session))
            return [];

        foreach (var row in cached.Chats)
        {
            if (!session.CachedChatIds.Add(row.Id))
                continue;

            var definition = Chat.Parser.ParseFrom(row.Definition);
            var title = string.IsNullOrWhiteSpace(row.Title) ? _localization.GetString("Messenger_Chat") : row.Title;
            var item = new ChatItemViewModel(definition, title, row.FirstName, row.LastName, "",
                row.PeerId, _localization, row.Username, row.AccountStatus, row.AvatarFileId);
            item.RestoreCachedPreview(row);
            if (definition.Muted)
                session.MutedChats[item.Id] = definition.MutedUntil?.ToDateTimeOffset();
            UpdateMuteIndicator(session, item);
            UpdateDraftPreview(item);
            Chats.Add(item);
        }
        ScheduleMuteExpiry(session);
        ApplyChatFilter();
        return cached.Folders;
    }

    private async Task<bool> ReconnectRestoredSessionAsync(RealtimeSession session, CancellationToken token)
    {
        var retry = TimeSpan.FromSeconds(2);
        while (IsCurrent(session))
        {
            await ResyncDelay(retry, token);
            if (!IsCurrent(session))
                return false;

            var restored = await _authentication!.TryRestoreSessionAsync(token);
            if (!IsCurrent(session))
                return false;
            if (!_authentication.IsRestoredSessionOffline)
            {
                if (!restored)
                {
                    var errorKey = _authentication.RestoreSessionErrorResourceKey;
                    var previousView = _navigation?.CurrentViewModel;
                    await ResetAfterSessionRejectionAsync();
                    if (_realtimeSession is null && session.UserId == _messenger.CurrentUserId &&
                        session.Node == _messenger.CurrentNodeAddress && _navigation is { } navigation &&
                        ReferenceEquals(previousView, navigation.CurrentViewModel))
                    {
                        navigation.ShowRestrictedLogin(errorKey);
                    }
                    return false;
                }

                IsOffline = false;
                StartSecretChats(session);
                await _outgoingReady;
                if (!IsCurrent(session))
                    return false;
                _ = _outgoing.WakeAsync(OutboxScope(session), token);
                return true;
            }

            retry = TimeSpan.FromSeconds(Math.Min(retry.TotalSeconds * 2, 30));
        }

        return false;
    }

    private async Task RefreshCachedDisplayAsync(RealtimeSession session, ChatItemViewModel item,
        Chat definition, CancellationToken token)
    {
        try
        {
            var display = await CreateChatItemAsync(definition, session.UserId, item).WaitAsync(token);
            if (IsCurrent(session) && Chats.Contains(item))
            {
                item.ApplyDisplay(display);
                ApplyChatFilter();
                ScheduleChatCacheSave(session);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Grpc.Core.RpcException) { }
    }

    private Action ObserveChatCache(RealtimeSession session)
    {
        if (_chatCache is null)
            return static () => { };

        var watched = new HashSet<ChatItemViewModel>();
        void ItemChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(ChatItemViewModel.Preview) or nameof(ChatItemViewModel.LastMessageAt) or
                nameof(ChatItemViewModel.UnreadCount) or nameof(ChatItemViewModel.FirstUnreadMessageId) or
                nameof(ChatItemViewModel.IsMuted))
                ScheduleChatCacheSave(session);
        }
        void ChatsChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.OldItems is not null)
            {
                foreach (ChatItemViewModel item in args.OldItems)
                {
                    if (watched.Remove(item))
                        item.PropertyChanged -= ItemChanged;
                }
            }
            if (args.NewItems is not null)
            {
                foreach (ChatItemViewModel item in args.NewItems)
                {
                    if (watched.Add(item))
                        item.PropertyChanged += ItemChanged;
                }
            }
            ScheduleChatCacheSave(session);
        }
        void FoldersChanged(object? sender, EventArgs args)
        {
            if (_chatFolders is { IsBusy: false, LoadError: null })
                ScheduleChatCacheSave(session);
        }
        Chats.CollectionChanged += ChatsChanged;
        if (_chatFolders is not null)
            _chatFolders.Changed += FoldersChanged;
        return () =>
        {
            Chats.CollectionChanged -= ChatsChanged;
            if (_chatFolders is not null)
                _chatFolders.Changed -= FoldersChanged;
            foreach (var item in watched)
                item.PropertyChanged -= ItemChanged;
        };
    }

    private void ScheduleChatCacheSave(RealtimeSession session)
    {
        if (_chatCache is null || !IsCurrent(session) || !session.CanWriteCache || session.CacheWriteScheduled)
            return;

        session.CacheWriteScheduled = true;
        PostSession(session, SaveChatCacheSnapshot);
    }

    private void SaveChatCacheSnapshot(RealtimeSession session)
    {
        if (!session.CacheWriteScheduled)
            return;
        session.CacheWriteScheduled = false;
        var snapshot = new CachedChatList(Chats.Select(chat => chat.ToCachedChat(session.MutedChats)).ToArray(),
            _chatFolders?.Folders.ToArray() ?? []);
        _chatCache!.ScheduleSave(CacheScope(session), snapshot);
    }

    public Task FlushChatCacheAsync()
    {
        if (_realtimeSession is { CacheWriteScheduled: true } session && IsCurrent(session))
            SaveChatCacheSnapshot(session);
        return _chatCache?.FlushAsync() ?? Task.CompletedTask;
    }

    public Task<bool> ClearChatCacheAsync()
    {
        if (_chatCache is null)
            return Task.FromResult(true);
        if (_realtimeSession is { } session)
        {
            session.CanWriteCache = false;
            session.CacheWriteScheduled = false;
            session.CanWriteHistoryEvents = false;
            session.ConfirmedHistoryTails.Clear();
            return _chatCache.ClearAsync(CacheScope(session));
        }
        return _messenger.CurrentUserId is { } userId
            ? _chatCache.ClearAsync(ChatListScope.Create(_messenger.CurrentNodeAddress, userId))
            : Task.FromResult(true);
    }

    public async Task ResetAfterSessionRejectionAsync()
    {
        var outgoingCleanup = ClearOutgoingForLogoutAsync();
        var cacheCleanup = ClearChatCacheAsync();
        var draftCleanup = StopDraftsForLogoutAsync();
        var secretCleanup = ClearSecretChatsAsync();
        Reset();
        await Task.WhenAll(outgoingCleanup, cacheCleanup, draftCleanup, secretCleanup);
    }
}

public sealed partial class ChatItemViewModel
{
    internal void RestoreCachedPreview(CachedChat row)
    {
        SnapshotLastMessageId = row.LastMessageId;
        SnapshotLastActivity = row.LastMessageAt;
        SetLastMessage(row.LastMessageId, row.Preview, row.LastMessageAt, row.EditedAt);
    }

    internal CachedChat ToCachedChat(Dictionary<string, DateTimeOffset?> mutedChats)
    {
        var definition = new Chat
        {
            Id = Id,
            Title = Definition.Title,
            IsGroupChat = Definition.IsGroupChat,
            ChatType = Definition.ChatType,
            IsFederated = Definition.IsFederated,
            PrivateInviteState = Definition.PrivateInviteState,
            PrivateInviterUserId = Definition.PrivateInviterUserId,
            KdfSalt = Definition.KdfSalt,
            PassphraseVerifier = Definition.PassphraseVerifier,
            HasDraft = Definition.HasDraft,
            CountUnread = UnreadCount,
            FirstUnreadMessageId = FirstUnreadMessageId,
            Muted = mutedChats.TryGetValue(Id, out var until),
            MutedUntil = until is { } expiry ? Timestamp.FromDateTimeOffset(expiry) : null
        };
        definition.Members.AddRange(Definition.Members.Select(member => new ChatMember
        {
            UserId = member.UserId,
            UserUuid = member.UserUuid,
            ServerName = member.ServerName
        }));
        return new CachedChat(Id, definition.ToByteArray(), Title, FirstName, LastName, Username, PeerUserId,
            AccountStatus, Preview, LastMessageId, LastMessageAt, LastMessageEditedAt)
        {
            AvatarFileId = AvatarFileId
        };
    }
}
