using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;
using Barkfluff.CloudMessaging.Services;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Barkfluff.CloudMessaging.Consumers;

/// <summary>
/// Consumer для обработки событий push-уведомлений из RabbitMQ.
/// </summary>
public class PushNotificationConsumer : IConsumer<PushNotificationEvent>
{
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly MessagesApi.MessagesApiClient _messagesClient;
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<PushNotificationConsumer> _logger;

    public PushNotificationConsumer(
        UsersServerApi.UsersServerApiClient usersClient,
        MessagesApi.MessagesApiClient messagesClient,
        FirebaseService firebaseService,
        ILogger<PushNotificationConsumer> logger)
    {
        _usersClient = usersClient;
        _messagesClient = messagesClient;
        _firebaseService = firebaseService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PushNotificationEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Обработка push-уведомления. ChatId: {ChatId}, MessageId: {MessageId}, Recipients: {Count}",
            message.ChatId,
            message.MessageId,
            message.RecipientUserIds.Count);

        if (message.RecipientUserIds.Count == 0)
        {
            _logger.LogWarning("Нет получателей для push-уведомления");
            return;
        }

        try
        {
            // Параллельно получаем данные отправителя и чата
            var senderCall = _usersClient.GetByIdAsync(
                new GetByIdRequest { UserId = message.SenderId });

            var chatInfoCall = _messagesClient.GetChatInfoAsync(
                new GetChatInfoRequest { ChatId = message.ChatId.ToString() });

            await System.Threading.Tasks.Task.WhenAll(senderCall.ResponseAsync, chatInfoCall.ResponseAsync);

            var senderResponse = senderCall.ResponseAsync.Result;
            var chatInfoResponse = chatInfoCall.ResponseAsync.Result;

            var senderName = senderResponse.User != null
                ? $"{senderResponse.User.FirstName} {senderResponse.User.LastName}".Trim()
                : "";
            if (string.IsNullOrWhiteSpace(senderName))
            {
                senderName = senderResponse.User?.Username ?? "Unknown";
            }

            var senderAvatarUrl = senderResponse.User?.ProfilePicturePreview ?? string.Empty;

            var isGroupChat = chatInfoResponse.IsGroupChat;
            var chatTitle = chatInfoResponse.Title ?? string.Empty;
            var chatAvatarUrl = chatInfoResponse.Picture ?? string.Empty;

            // Получаем FCM токены устройств получателей
            var tokensResponse = await _usersClient.GetDevicesWithFirebaseTokensAsync(
                new GetDevicesWithFirebaseTokensRequest
                {
                    UserIds = { message.RecipientUserIds }
                });

            if (tokensResponse.Tokens.Count == 0)
            {
                _logger.LogDebug("Нет устройств с Firebase токенами у получателей");
                return;
            }

            // Отправляем push-уведомления
            foreach (var token in tokensResponse.Tokens)
            {
                await _firebaseService.SendNotificationAsync(
                    token.FirebaseToken,
                    senderName,
                    message.MessageText ?? string.Empty,
                    message.ChatId.ToString(),
                    message.SenderId,
                    message.MessageId,
                    senderAvatarUrl,
                    chatTitle,
                    chatAvatarUrl,
                    isGroupChat,
                    message.ContentType,
                    message.ImagePreviewUrl,
                    message.AttachmentCount);
            }

            _logger.LogInformation(
                "Push-уведомления отправлены. Отправитель: {SenderName}, Устройств: {Count}, IsGroupChat: {IsGroupChat}",
                senderName,
                tokensResponse.Tokens.Count,
                isGroupChat);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Ошибка при обработке push-уведомления. ChatId: {ChatId}, MessageId: {MessageId}",
                message.ChatId,
                message.MessageId);
            throw;
        }
    }
}
