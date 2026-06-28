using Barkfluff.CloudMessaging.Services;

using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

namespace Barkfluff.CloudMessaging.Consumers;

/// <summary>
/// Consumer завершения ринга: гасит нотификацию входящего звонка (type=dismiss_call)
/// на всех FCM-устройствах получателей (accepted/rejected/ended/timeout/busy).
/// </summary>
public class CallDismissPushConsumer : IConsumer<CallDismissPushEvent>
{
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<CallDismissPushConsumer> _logger;

    public CallDismissPushConsumer(
        UsersServerApi.UsersServerApiClient usersClient,
        FirebaseService firebaseService,
        ILogger<CallDismissPushConsumer> logger)
    {
        _usersClient = usersClient;
        _firebaseService = firebaseService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CallDismissPushEvent> context)
    {
        var message = context.Message;

        _logger.LogDebug(
            "Обработка dismiss_call. CallId: {CallId}, Reason: {Reason}, Recipients: {Count}",
            message.CallId,
            message.Reason,
            message.RecipientUserIds.Count);

        if (message.RecipientUserIds.Count == 0)
        {
            return;
        }

        try
        {
            var tokensResponse = await _usersClient.GetDevicesWithFirebaseTokensAsync(
                new GetDevicesWithFirebaseTokensRequest { UserIds = { message.RecipientUserIds } },
                cancellationToken: context.CancellationToken);

            var fcmTokens = tokensResponse.Tokens
                .Select(t => t.FirebaseToken)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (fcmTokens.Count == 0)
            {
                return;
            }

            await _firebaseService.SendCallDismissBatchAsync(
                fcmTokens,
                message.CallId.ToString(),
                message.Reason,
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке dismiss_call. CallId: {CallId}", message.CallId);
        }
    }
}
