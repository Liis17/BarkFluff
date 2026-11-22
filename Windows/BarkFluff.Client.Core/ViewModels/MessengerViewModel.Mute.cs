using BarkFluff.Proto.Users;
using BarkFluff.WebApi.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly TimeProvider _muteClock;

    public bool IsChatMuted(string chatId) =>
        _realtimeSession is { } session && IsCurrent(session) && IsChatMuted(session, chatId, _muteClock.GetUtcNow());

    private static bool IsChatMuted(RealtimeSession session, string chatId, DateTimeOffset now) =>
        session.MutedChats.TryGetValue(chatId, out var until) && (until is null || until > now);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleChatMuteAsync(ChatItemViewModel? chat)
    {
        if (_realtimeSession is not { } session || !IsCurrent(session) || chat is null ||
            !Chats.Contains(chat) || chat.IsChangingMute)
        {
            return;
        }

        var wasStored = session.MutedChats.TryGetValue(chat.Id, out var previousExpiry);
        var muted = !IsChatMuted(chat.Id);
        var token = session.Lifetime.Token;
        ActionError = null;
        chat.IsChangingMute = true;
        SetMute(session.MutedChats, chat.Id, muted, null);
        ScheduleMuteExpiry(session);

        ErrorReturner? error;
        try
        {
            error = await _messenger.SetChatMutedAsync(chat.Id, muted, token).WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            error = new ErrorReturner(false);
        }

        PostSession(session, current =>
        {
            // Снимок, запущенный до ответа на запись, может содержать прежнее значение.
            chat.MuteSnapshotBarrier = current.ChatSnapshotRequest;
            chat.IsChangingMute = false;
            if (error is not { IsSuccess: true })
            {
                SetMute(current.MutedChats, chat.Id, wasStored, previousExpiry);
                ScheduleMuteExpiry(current);
                ActionError = _localization.GetString("Messenger_ChatMuteFailed");
            }
        });
    }

    private async Task RefreshMuteSnapshotAsync(RealtimeSession session, long request)
    {
        if (session.PendingMuteHints is not null)
        {
            return;
        }

        var hints = session.PendingMuteHints = new Dictionary<string, long>();
        var token = session.Lifetime.Token;
        List<MutedChat>? mutedChats = null;
        try
        {
            var (error, chats) = await _messenger.GetMutedChatsAsync(token).WaitAsync(token);
            if (error is { IsSuccess: true })
            {
                mutedChats = chats;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // При недоступном Users сохраняем известные mute и учитываем флаги ListChats.
        }

        PostSession(session, current =>
        {
            current.PendingMuteHints = null;
            current.HasAuthoritativeMutes = mutedChats is not null;
            if (mutedChats is not null)
            {
                ApplyMuteSnapshot(current, request, mutedChats);
            }
            else
            {
                foreach (var chat in Chats)
                {
                    if (hints.TryGetValue(chat.Id, out var hintRequest))
                    {
                        ApplyMuteHint(current, chat, hintRequest);
                    }
                }
            }
        });
    }

    private void ObserveMuteHint(RealtimeSession session, ChatItemViewModel chat, long request, bool muted)
    {
        if (!muted)
        {
            return;
        }

        if (session.PendingMuteHints is { } hints)
        {
            hints[chat.Id] = request;
        }
        else if (!session.HasAuthoritativeMutes)
        {
            ApplyMuteHint(session, chat, request);
        }
    }

    private void ApplyMuteHint(RealtimeSession session, ChatItemViewModel chat, long request)
    {
        if (!chat.IsChangingMute && chat.MuteSnapshotBarrier < request &&
            session.MutedChats.TryAdd(chat.Id, null))
        {
            UpdateMuteIndicator(session, chat);
        }
    }

    private void ApplyMuteSnapshot(RealtimeSession session, long request, List<MutedChat> allMutes)
    {
        var snapshot = allMutes.ToDictionary(chat => chat.ChatId, chat => chat.MutedUntil?.ToDateTimeOffset());

        foreach (var chat in Chats)
        {
            if (chat.IsChangingMute || chat.MuteSnapshotBarrier >= request)
            {
                var muted = session.MutedChats.TryGetValue(chat.Id, out var until);
                SetMute(snapshot, chat.Id, muted, until);
            }
        }

        session.MutedChats = snapshot;
        ScheduleMuteExpiry(session);
    }

    private static void SetMute(Dictionary<string, DateTimeOffset?> mutes, string chatId, bool muted, DateTimeOffset? until)
    {
        if (muted)
        {
            mutes[chatId] = until;
        }
        else
        {
            mutes.Remove(chatId);
        }
    }

    private void UpdateMuteIndicator(RealtimeSession session, ChatItemViewModel chat) =>
        chat.IsMuted = IsChatMuted(session, chat.Id, _muteClock.GetUtcNow());

    private void ScheduleMuteExpiry(RealtimeSession session)
    {
        var now = _muteClock.GetUtcNow();
        foreach (var chat in Chats)
        {
            chat.IsMuted = IsChatMuted(session, chat.Id, now);
        }
        var next = session.MutedChats.Values.Where(until => until > now).Min();
        if (next is null)
        {
            session.MuteExpiryTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        session.MuteExpiryTimer ??= _muteClock.CreateTimer(_ => PostSession(session, ScheduleMuteExpiry),
            null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        // Платформенный таймер ограничивает максимальный интервал.
        var delay = next.Value - now;
        session.MuteExpiryTimer.Change(TimeSpan.FromMilliseconds(Math.Clamp(
            Math.Ceiling(delay.TotalMilliseconds), 1, uint.MaxValue - 1)), Timeout.InfiniteTimeSpan);
    }
}

public sealed partial class ChatItemViewModel
{
    internal long MuteSnapshotBarrier { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteMenuHeader), nameof(HasMutedUnreadMessages), nameof(HasUnmutedUnreadMessages))]
    private bool _isMuted;
    [ObservableProperty] private bool _isChangingMute;

    public string MuteMenuHeader => _localization.GetString(IsMuted ? "Messenger_UnmuteChat" : "Messenger_MuteChat");
    public bool HasMutedUnreadMessages => HasUnreadMessages && IsMuted;
    public bool HasUnmutedUnreadMessages => HasUnreadMessages && !IsMuted;
}
