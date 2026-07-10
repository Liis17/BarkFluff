using Barkfluff.CloudMessaging.Services;

using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

namespace Barkfluff.CloudMessaging.Consumers;

/// <summary>
/// Consumer запроса на приватный чат: шлёт high-priority data-only FCM push
/// (type=private_chat_invite) приглашённому, чтобы запрос дошёл при background/killed app.
/// </summary>
public class PrivateChatInvitePushConsumer : IConsumer<PrivateChatInviteEvent>
{
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<PrivateChatInvitePushConsumer> _logger;

    public PrivateChatInvitePushConsumer(
        UsersServerApi.UsersServerApiClient usersClient,
        FirebaseService firebaseService,
        ILogger<PrivateChatInvitePushConsumer> logger)
    {
        _usersClient = usersClient;
        _firebaseService = firebaseService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PrivateChatInviteEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Обработка private_chat_invite push. ChatId: {ChatId}, Inviter: {InviterId}, Invitee: {InviteeId}",
            message.ChatId,
            message.InviterUserId,
            message.InviteeUserId);

        try
        {
            var inviterResponse = await _usersClient.GetByIdAsync(
                new GetByIdRequest { UserId = message.InviterUserId },
                cancellationToken: context.CancellationToken);

            var inviterName = inviterResponse.User != null
                ? $"{inviterResponse.User.FirstName} {inviterResponse.User.LastName}".Trim()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(inviterName))
            {
                inviterName = inviterResponse.User?.Username ?? "BarkFluff";
            }

            var avatarUrl = inviterResponse.User?.ProfilePicturePreview ?? string.Empty;

            var tokensResponse = await _usersClient.GetDevicesWithFirebaseTokensAsync(
                new GetDevicesWithFirebaseTokensRequest { UserIds = { message.InviteeUserId } },
                cancellationToken: context.CancellationToken);

            var fcmTokens = tokensResponse.Tokens
                .Select(t => t.FirebaseToken)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (fcmTokens.Count == 0)
            {
                _logger.LogDebug("Нет устройств с Firebase токенами у приглашённого {InviteeId}", message.InviteeUserId);
                return;
            }

            await _firebaseService.SendPrivateChatInviteBatchAsync(
                fcmTokens,
                message.ChatId.ToString(),
                message.InviterUserId,
                inviterName,
                avatarUrl,
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            // best-effort: не пробрасываем, иначе MassTransit ретраит бесполезно.
            _logger.LogError(ex, "Ошибка при обработке private_chat_invite push. ChatId: {ChatId}", message.ChatId);
        }
    }
}
