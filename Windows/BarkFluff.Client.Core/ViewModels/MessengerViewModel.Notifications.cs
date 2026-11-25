using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels.Settings;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class MessengerViewModel
{
    private readonly MessageNotificationService? _notifications;
    private readonly SettingsNotificationsViewModel? _notificationSettings;
    private bool _notificationWindowActive;
    private bool _notificationMessengerVisible;
    private long _notificationActivation;

    public void SetNotificationWindowState(bool active, bool messengerVisible)
    {
        var wasVisible = _notificationMessengerVisible;
        var wasActive = _notificationWindowActive;
        _notificationWindowActive = active;
        _notificationMessengerVisible = messengerVisible;
        if (messengerVisible && (!wasVisible || active && !wasActive))
        {
            if (SecretChats.IsOpen)
                ClearSecretChatNotification();
            else
                ClearChatNotifications(SelectedChat);
        }
    }

    private void ClearChatNotifications(ChatItemViewModel? chat)
    {
        if (chat is not null && IsCurrent(_realtimeSession) && _realtimeSession is { } session)
        {
            _notifications?.ClearChat(new(session.Node, session.UserId, chat.Id));
        }
    }

    private async Task NotifyIncomingAsync(RealtimeSession session, MessageUpdate update, ChatItemViewModel? chat)
    {
        if (_notifications is null || !_notifications.RememberIncoming(update, session.UserId))
        {
            return;
        }

        bool IsChatVisible() => _notificationMessengerVisible && !SecretChats.IsOpen && SelectedChat?.Id == update.ChatId;
        if (_notificationSettings is null || IsChatVisible() && _notificationWindowActive || IsChatMuted(update.ChatId))
        {
            return;
        }

        var token = session.Lifetime.Token;
        try
        {
            await Task.WhenAll(_notificationSettings.PendingLoad, session.NotificationMutesReady.Task).WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }

        if (!IsCurrent(session))
        {
            return;
        }

        chat ??= Chats.FirstOrDefault(item => item.Id == update.ChatId);
        var clearVersion = _notifications.ClearVersion;
        bool CanShow() => IsCurrent(session) && clearVersion == _notifications.ClearVersion &&
            (session.HasAuthoritativeMutes || chat is not null) && MessageNotificationService.Allows(
                _notificationSettings.IsEnabled, IsChatMuted(update.ChatId), IsChatVisible(), _notificationWindowActive);

        if (!CanShow())
        {
            return;
        }

        string? sender = null;
        try
        {
            if (!update.IsPrivate && chat is null && session.ChatSnapshot is { } snapshot)
            {
                // Обновление списка уже запущено обработчиком realtime.
                await snapshot.WaitAsync(token);
                if (!CanShow())
                {
                    return;
                }
                chat = Chats.FirstOrDefault(item => item.Id == update.ChatId);
            }

            if (!update.IsPrivate && chat?.Definition.IsGroupChat == true && update.Message is { } message)
            {
                if (!session.NotificationSenders.TryGetValue(message.SenderId, out var resolving))
                {
                    resolving = ResolveNotificationSenderAsync(message.SenderId, token);
                    if (session.NotificationSenders.Count == 256)
                    {
                        session.NotificationSenders.Clear();
                    }
                    session.NotificationSenders.Add(message.SenderId, resolving);
                }
                sender = await resolving.WaitAsync(token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // При недоступном снимке превью всё равно можно показать.
        }

        PostSession(session, _ =>
        {
            if (CanShow())
            {
                _notifications.Show(new(session.Node, session.UserId, update.ChatId), update, chat, sender);
            }
        });
    }

    private async Task<string?> ResolveNotificationSenderAsync(long senderId, CancellationToken token)
    {
        try
        {
            var (error, user) = await _messenger.GetUserDataAsync(senderId, token).WaitAsync(token);
            return error is { IsSuccess: true } && user is not null ? $"{user.FirstName} {user.LastName}".Trim() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public Task<bool> OpenNotificationChatAsync(MessageNotificationTarget target)
    {
        var session = _realtimeSession;
        return session is not null && IsCurrent(session) && target.Matches(session.Node, session.UserId)
            ? target.ChatId.StartsWith("secret:", StringComparison.Ordinal)
                ? Task.FromResult(SecretChats.OpenChat(target.ChatId["secret:".Length..]))
                : OpenChatByIdAsync(session, target.ChatId, session.Lifetime.Token)
            : Task.FromResult(false);
    }

    private async Task<bool> OpenChatByIdAsync(
        RealtimeSession session, string chatId, CancellationToken token, Func<bool>? stillRequested = null)
    {
        var activation = ++_notificationActivation;
        var version = _chatVersion;
        bool CanOpen() => IsCurrent(session) && version == _chatVersion && activation == _notificationActivation &&
            !token.IsCancellationRequested && stillRequested?.Invoke() != false;
        var chat = Chats.FirstOrDefault(item => item.Id == chatId);
        if (chat is null)
        {
            try
            {
                var definition = await _messenger.FindChatAsync(chatId, token).WaitAsync(token);
                if (!CanOpen() || definition is null)
                {
                    return false;
                }

                var item = await CreateChatItemAsync(definition, session.UserId).WaitAsync(token);
                if (!CanOpen())
                {
                    return false;
                }

                chat = Chats.FirstOrDefault(existing => existing.Id == chatId);
                if (chat is null)
                {
                    chat = item;
                    UpdateMuteIndicator(session, item);
                    UpdateDraftPreview(item);
                    Chats.Add(item);
                    ApplyChatFilter();
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        if (!CanOpen())
            return false;

        IsProfileVisible = false;
        if (ReferenceEquals(SelectedChat, chat))
        {
            ClearChatNotifications(chat);
        }
        else
        {
            SelectedChat = chat;
        }

        return true;
    }
}
