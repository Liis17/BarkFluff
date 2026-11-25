using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;

namespace BarkFluff.Client.Core.Services;

internal sealed class NotificationReplyService(
    IMessengerService messenger, OutgoingMessageQueue queue, MessageNotificationService notifications)
{
    internal static bool CanReply(ChatItemViewModel? chat) =>
        chat is { CanSend: true, Definition.ChatType: ChatType.Regular };

    internal async Task<string?> AcceptAsync(NotificationReply reply, ChatItemViewModel? knownChat,
        Task queueReady, Func<bool> isCurrent, CancellationToken token)
    {
        bool Matches() => !token.IsCancellationRequested && isCurrent() &&
            reply.Target.Matches(messenger.CurrentNodeAddress, messenger.CurrentUserId);

        if (!Matches())
            return "Notifications_ReplySessionMismatch";
        if (reply.OperationId == Guid.Empty || reply.MessageId <= 0 ||
            reply.Target.ChatId.StartsWith("secret:", StringComparison.Ordinal))
            return "Notifications_ReplyUnavailable";

        var text = reply.Text.Trim();
        if (text.Length == 0)
            return "Notifications_ReplyEmpty";

        try
        {
            await queueReady.WaitAsync(token);
            if (!Matches())
                return "Notifications_ReplySessionMismatch";
            var canReply = knownChat is not null
                ? CanReply(knownChat)
                : await CanReplyAsync(reply.Target, token);
            if (!Matches())
                return "Notifications_ReplySessionMismatch";
            if (!canReply)
                return "Notifications_ReplyUnavailable";

            var scope = OutgoingScope.Create(reply.Target.Node, reply.Target.UserId);
            var accepted = await queue.EnqueueNotificationReplyAsync(scope, reply with { Text = text }, token);
            if (Matches())
            {
                notifications.RemoveReply(reply);
                if (accepted)
                    _ = MarkReadAsync(reply.MessageId, token);
            }
            return null;
        }
        catch (OperationCanceledException) when (!Matches())
        {
            return "Notifications_ReplySessionMismatch";
        }
        catch (Exception)
        {
            return "Notifications_ReplyFailed";
        }
    }

    private async Task<bool> CanReplyAsync(MessageNotificationTarget target, CancellationToken token)
    {
        var chat = await messenger.FindChatAsync(target.ChatId, token);
        if (chat is not { ChatType: ChatType.Regular } || chat.Id != target.ChatId)
            return false;
        var peer = chat.IsGroupChat ? null : chat.Members.FirstOrDefault(member => member.UserId != target.UserId);
        if (peer is null)
            return true;
        token.ThrowIfCancellationRequested();
        var (error, user) = await messenger.GetUserDataAsync(peer.UserId, token);
        return error is { IsSuccess: true } && user is not null &&
            user.AccountStatus is not (AccountStatus.Blocked or AccountStatus.PendingDeletion or AccountStatus.Deleted);
    }

    private async Task MarkReadAsync(long messageId, CancellationToken token)
    {
        try
        {
            await messenger.MarkMessagesReadAsync([messageId], token);
        }
        catch (Exception)
        {
            // Ошибка отметки о прочтении не отменяет сохранённый ответ.
        }
    }
}
