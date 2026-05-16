using Barkfluff.CloudMessaging.Services;

using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

namespace Barkfluff.CloudMessaging.Consumers;

/// <summary>
/// Consumer для админ-рассылок push-уведомлений из RabbitMQ.
/// Получает список FCM-токенов через Users (по DeviceId или все) и шлёт батчем.
/// </summary>
public class AdminBroadcastConsumer : IConsumer<AdminBroadcastNotificationEvent>
{
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<AdminBroadcastConsumer> _logger;

    public AdminBroadcastConsumer(
        UsersServerApi.UsersServerApiClient usersClient,
        FirebaseService firebaseService,
        ILogger<AdminBroadcastConsumer> logger)
    {
        _usersClient = usersClient;
        _firebaseService = firebaseService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AdminBroadcastNotificationEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Обработка admin-broadcast. Title: {Title}, TargetDeviceIds: {Count}",
            message.Title,
            message.TargetDeviceIds.Count);

        if (string.IsNullOrWhiteSpace(message.Title) || string.IsNullOrWhiteSpace(message.Body))
        {
            _logger.LogWarning("Admin broadcast пропущен: пустой Title или Body");
            return;
        }

        try
        {
            GetDevicesWithFirebaseTokensResponse tokensResponse;

            if (message.TargetDeviceIds.Count == 0)
            {
                tokensResponse = await _usersClient.GetAllDevicesWithFirebaseTokensAsync(
                    new GetAllDevicesWithFirebaseTokensRequest(),
                    cancellationToken: context.CancellationToken);
            }
            else
            {
                var request = new GetDevicesWithFirebaseTokensByDeviceIdsRequest();
                request.DeviceIds.AddRange(message.TargetDeviceIds.Select(g => g.ToString()));

                tokensResponse = await _usersClient.GetDevicesWithFirebaseTokensByDeviceIdsAsync(
                    request,
                    cancellationToken: context.CancellationToken);
            }

            var fcmTokens = tokensResponse.Tokens
                .Select(t => t.FirebaseToken)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (fcmTokens.Count == 0)
            {
                _logger.LogInformation("Admin broadcast: нет устройств с FCM-токенами для рассылки");
                return;
            }

            await _firebaseService.SendAdminBroadcastBatchAsync(
                fcmTokens,
                message.Title,
                message.Body,
                message.ImageUrl,
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            // Логируем и не пробрасываем — MassTransit не должен ретраить
            // рассылку (повторная отправка спамила бы пользователей).
            _logger.LogError(ex, "Ошибка обработки admin-broadcast. Title: {Title}", message.Title);
        }
    }
}
