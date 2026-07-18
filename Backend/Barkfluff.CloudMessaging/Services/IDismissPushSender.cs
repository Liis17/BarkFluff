using FirebaseAdmin.Messaging;

namespace Barkfluff.CloudMessaging.Services;

public interface IDismissPushSender
{
    Task<IReadOnlyList<DismissPushSendResult>> SendAsync(
        IReadOnlyList<string> fcmTokens,
        string chatId,
        CancellationToken cancellationToken);
}

public sealed record DismissPushSendResult(
    bool IsSuccess,
    MessagingErrorCode? ErrorCode = null,
    Exception? Exception = null);

internal sealed class FirebaseDismissPushSender(FirebaseMessaging messaging) : IDismissPushSender
{
    public async Task<IReadOnlyList<DismissPushSendResult>> SendAsync(
        IReadOnlyList<string> fcmTokens,
        string chatId,
        CancellationToken cancellationToken)
    {
        var response = await messaging.SendEachForMulticastAsync(new MulticastMessage
        {
            Tokens = [.. fcmTokens],
            Data = new Dictionary<string, string>
            {
                ["type"] = "dismiss_chat_notifications",
                ["chat_id"] = chatId
            },
            Android = new AndroidConfig
            {
                Priority = Priority.High
            }
        }, cancellationToken);

        return response.Responses
            .Select(item => new DismissPushSendResult(
                item.IsSuccess,
                item.Exception?.MessagingErrorCode,
                item.Exception))
            .ToList();
    }
}
