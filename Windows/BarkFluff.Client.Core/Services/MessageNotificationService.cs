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

public sealed record MessageNotification(MessageNotificationTarget Target, string Title, string Text);

public interface INotificationPresenter
{
    void Show(MessageNotification notification);
    void RemoveChat(MessageNotificationTarget target);
    void RemoveAll();
}

public sealed class MessageNotificationService(INotificationPresenter presenter, ILocalizationService localization)
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

    internal void Show(MessageNotificationTarget target, MessageUpdate update, ChatItemViewModel? chat, string? sender)
    {
        if (update.IsPrivate)
        {
            presenter.Show(new(target, "BarkFluff", localization.GetString("Notifications_PrivateMessage")));
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

        presenter.Show(new(target, title, preview));
    }

    internal void ClearChat(MessageNotificationTarget target) => presenter.RemoveChat(target);
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
