using Barkfluff.CloudMessaging.Services;

using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

namespace Barkfluff.CloudMessaging.Consumers;

/// <summary>
/// Consumer для команды dismiss: убирает push-нотификацию чата
/// на всех FCM-устройствах указанного пользователя.
/// </summary>
public class DismissPushConsumer : IConsumer<DismissPushEvent>
{
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<DismissPushConsumer> _logger;

    public DismissPushConsumer(
        UsersServerApi.UsersServerApiClient usersClient,
        FirebaseService firebaseService,
        ILogger<DismissPushConsumer> logger)
    {
        _usersClient = usersClient;
        _firebaseService = firebaseService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DismissPushEvent> context)
    {
        var message = context.Message;

        _logger.LogDebug(
            "Обработка dismiss push. UserId={UserId}, ChatId={ChatId}",
            message.UserId,
            message.ChatId);

        try
        {
            var tokensResponse = await _usersClient.GetDevicesWithFirebaseTokensAsync(
                new GetDevicesWithFirebaseTokensRequest
                {
                    UserIds = { message.UserId }
                },
                cancellationToken: context.CancellationToken);

            var fcmTokens = tokensResponse.Tokens
                .Select(t => t.FirebaseToken)
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (fcmTokens.Count == 0)
            {
                _logger.LogDebug(
                    "Нет устройств с Firebase токенами для UserId={UserId}, dismiss пропущен",
                    message.UserId);
                return;
            }

            await _firebaseService.SendDismissBatchAsync(
                fcmTokens,
                message.ChatId.ToString(),
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            // best-effort: не пробрасываем, иначе MassTransit будет ретраить бесполезно
            _logger.LogError(
                ex,
                "Ошибка при обработке dismiss push. UserId={UserId}, ChatId={ChatId}",
                message.UserId,
                message.ChatId);
        }
    }
}
