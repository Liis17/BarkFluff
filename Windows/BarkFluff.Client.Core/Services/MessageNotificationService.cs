using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Shared;

namespace BarkFluff.Client.Core.Services;

public sealed record MessageNotificationTarget(string Node, long UserId, string ChatId)
{
    public bool Matches(string node, long? userId) =>
        Node.Length != 0 && Node == node && UserId > 0 && UserId == userId &&
        !string.IsNullOrWhiteSpace(ChatId);
}

public sealed record MessageNotification(MessageNotificationTarget Target, string Title, string Text,
    NotificationReplyAction? Reply = null, ChatNotificationSound Sound = ChatNotificationSound.Default);

public sealed record NotificationReplyAction(long MessageId, string Label, string Placeholder);
public sealed record NotificationReply(MessageNotificationTarget Target, Guid OperationId, long MessageId, string Text);

public interface INotificationPresenter
{
    void Show(MessageNotification notification);
    void PreviewSound(ChatNotificationSound sound, string text);
    void RemoveChat(MessageNotificationTarget target);
    void RemoveAll();
    void RemoveReply(NotificationReply reply);
}

public sealed class MessageNotificationService(INotificationPresenter presenter, ILocalizationService localization,
    IChatNotificationSoundStore? sounds = null)
{
    private const int RecentMessageLimit = 4096;
    private readonly HashSet<(string ChatId, bool Private, long Id)> _seen = [];
    private readonly Queue<(string ChatId, bool Private, long Id)> _recent = [];

    internal bool RememberIncoming(MessageUpdate update, long userId)
    {
        var sender = update.Message?.SenderId ?? update.Encrypted?.SenderId;
        if (update.Kind != MessageUpdateKind.New || update.MessageId <= 0 ||
            sender is null or <= 0 || sender == userId ||
            update.Message is { IsSystemMessage: true } or { Type: MessageContentType.System } ||
            update.Encrypted?.IsDeleted == true)
        {
            return false;
        }

        var key = (update.ChatId, update.IsPrivate, update.MessageId);
        if (!_seen.Add(key))
        {
            return false;
        }

        _recent.Enqueue(key);
        if (_recent.Count > RecentMessageLimit)
        {
            _seen.Remove(_recent.Dequeue());
        }

        return true;
    }

    internal static bool Allows(bool enabled, bool muted, bool chatVisible, bool windowActive) =>
        enabled && !muted && !(chatVisible && windowActive);

    internal bool CanEditSound => sounds is not null;

    internal Task<ChatNotificationSound> LoadSoundAsync(MessageNotificationTarget target, CancellationToken token) =>
        sounds?.LoadAsync(target, token) ?? Task.FromResult(ChatNotificationSound.Default);

    internal async Task<ChatNotificationSound> ResolveSoundAsync(MessageNotificationTarget target, CancellationToken token)
    {
        try
        {
            return await LoadSoundAsync(target, token);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return ChatNotificationSound.Default;
        }
    }

    internal Task SaveSoundAsync(MessageNotificationTarget target, ChatNotificationSound sound, CancellationToken token) =>
        sounds!.SaveAsync(target, sound, token);

    internal void PreviewSound(ChatNotificationSound sound, bool muted)
    {
        if (!muted && sound != ChatNotificationSound.Silent)
            presenter.PreviewSound(sound, localization.GetString("Messenger_ChatSoundPreviewTitle"));
    }

    internal void Show(MessageNotificationTarget target, MessageUpdate update, ChatItemViewModel? chat, string? sender,
        ChatNotificationSound sound)
    {
        if (update.IsPrivate)
        {
            presenter.Show(new(target, "BarkFluff", localization.GetString("Notifications_PrivateMessage"), Sound: sound));
            return;
        }

        var title = chat?.Title ?? localization.GetString("Messenger_Chat");
        if (!string.IsNullOrWhiteSpace(sender))
        {
            title = $"{title} · {sender}";
        }

        var preview = ChatItemViewModel.CreatePreview(update.Message?.Text ?? string.Empty, 20);
        if (preview.Length == 0)
        {
            preview = localization.GetString(update.Message?.Attachments.Count > 0
                ? "Messenger_Attachment" : "Notifications_NewMessage");
        }

        var reply = NotificationReplyService.CanReply(chat)
            ? new NotificationReplyAction(update.MessageId, localization.GetString("Notifications_Reply"),
                localization.GetString("Notifications_ReplyPlaceholder"))
            : null;
        presenter.Show(new(target, title, preview, reply, sound));
    }

    internal void ShowSecret(MessageNotificationTarget target) =>
        presenter.Show(new(target, "BarkFluff", localization.GetString("Secret_Notification")));

    internal void ClearChat(MessageNotificationTarget target) => presenter.RemoveChat(target);
    internal void RemoveReply(NotificationReply reply) => presenter.RemoveReply(reply);
    internal long ClearVersion { get; private set; }

    internal void Clear()
    {
        ClearVersion++;
        presenter.RemoveAll();
    }

    internal void EndSession()
    {
        _seen.Clear();
        _recent.Clear();
        Clear();
    }
}
