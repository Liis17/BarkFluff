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
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<PushNotificationConsumer> _logger;

    public PushNotificationConsumer(
        UsersServerApi.UsersServerApiClient usersClient,
        FirebaseService firebaseService,
        ILogger<PushNotificationConsumer> logger)
    {
        _usersClient = usersClient;
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
            // Получаем имя отправителя
            var senderResponse = await _usersClient.GetByIdAsync(
                new GetByIdRequest { UserId = message.SenderId });

            var senderName = senderResponse.User != null
                ? $"{senderResponse.User.FirstName} {senderResponse.User.LastName}".Trim()
                : "Unknown";

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
                    message.SenderId);
            }

            _logger.LogInformation(
                "Push-уведомления отправлены. Отправитель: {SenderName}, Устройств: {Count}",
                senderName,
                tokensResponse.Tokens.Count);
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
