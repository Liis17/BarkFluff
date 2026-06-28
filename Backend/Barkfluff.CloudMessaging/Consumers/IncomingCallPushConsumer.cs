using Barkfluff.CloudMessaging.Services;

using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

namespace Barkfluff.CloudMessaging.Consumers;

/// <summary>
/// Consumer входящего звонка: формирует high-priority data-only FCM push (type=incoming_call)
/// получателям, чтобы ринг дошёл при background/killed app.
/// </summary>
public class IncomingCallPushConsumer : IConsumer<IncomingCallPushEvent>
{
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly MessagesApi.MessagesApiClient _messagesClient;
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<IncomingCallPushConsumer> _logger;

    public IncomingCallPushConsumer(
        UsersServerApi.UsersServerApiClient usersClient,
        MessagesApi.MessagesApiClient messagesClient,
        FirebaseService firebaseService,
        ILogger<IncomingCallPushConsumer> logger)
    {
        _usersClient = usersClient;
        _messagesClient = messagesClient;
        _firebaseService = firebaseService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IncomingCallPushEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Обработка incoming_call push. CallId: {CallId}, Caller: {CallerId}, Recipients: {Count}",
            message.CallId,
            message.CallerUserId,
            message.RecipientUserIds.Count);

        if (message.RecipientUserIds.Count == 0)
        {
            return;
        }

        try
        {
            var callerResponse = await _usersClient.GetByIdAsync(
                new GetByIdRequest { UserId = message.CallerUserId },
                cancellationToken: context.CancellationToken);

            var callerName = callerResponse.User != null
                ? $"{callerResponse.User.FirstName} {callerResponse.User.LastName}".Trim()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(callerName))
            {
                callerName = callerResponse.User?.Username ?? "BarkFluff";
            }

            var avatarUrl = callerResponse.User?.ProfilePicturePreview ?? string.Empty;

            var chatId = message.ChatId?.ToString() ?? string.Empty;
            var chatTitle = string.Empty;
            if (message.ChatId.HasValue)
            {
                var chatInfo = await _messagesClient.GetChatInfoAsync(
                    new GetChatInfoRequest { ChatId = chatId },
                    cancellationToken: context.CancellationToken);
                chatTitle = chatInfo.Title ?? string.Empty;
            }

            var tokensResponse = await _usersClient.GetDevicesWithFirebaseTokensAsync(
                new GetDevicesWithFirebaseTokensRequest { UserIds = { message.RecipientUserIds } },
                cancellationToken: context.CancellationToken);

            var fcmTokens = tokensResponse.Tokens
                .Select(t => t.FirebaseToken)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (fcmTokens.Count == 0)
            {
                _logger.LogDebug("Нет устройств с Firebase токенами у получателей звонка {CallId}", message.CallId);
                return;
            }

            var startedAtUnix = new DateTimeOffset(
                DateTime.SpecifyKind(message.StartedAt, DateTimeKind.Utc)).ToUnixTimeSeconds();

            await _firebaseService.SendIncomingCallBatchAsync(
                fcmTokens,
                message.CallId.ToString(),
                message.CallerUserId,
                chatId,
                message.MediaType,
                startedAtUnix,
                callerName,
                avatarUrl,
                chatTitle,
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            // best-effort: не пробрасываем, иначе MassTransit ретраит бесполезно.
            _logger.LogError(ex, "Ошибка при обработке incoming_call push. CallId: {CallId}", message.CallId);
        }
    }
}
