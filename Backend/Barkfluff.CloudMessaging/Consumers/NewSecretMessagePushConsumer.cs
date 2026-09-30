using Barkfluff.CloudMessaging.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;
using MassTransit;

namespace Barkfluff.CloudMessaging.Consumers;

public class NewSecretMessagePushConsumer(
    UsersServerApi.UsersServerApiClient usersClient,
    FirebaseService firebaseService,
    ILogger<NewSecretMessagePushConsumer> logger) : IConsumer<NewSecretMessageEvent>
{
    public async Task Consume(ConsumeContext<NewSecretMessageEvent> context)
    {
        var @event = context.Message;
        try
        {
            var response = await usersClient.GetDevicesWithFirebaseTokensByDeviceIdsAsync(
                new GetDevicesWithFirebaseTokensByDeviceIdsRequest
                {
                    DeviceIds = { @event.RecipientDeviceId.ToString() }
                }, cancellationToken: context.CancellationToken);
            var tokens = response.Tokens
                .Where(token => token.PushPlatform != PushPlatform.Web &&
                    Guid.TryParse(token.DeviceId, out var deviceId) && deviceId == @event.RecipientDeviceId)
                .Select(token => token.FirebaseToken)
                .Where(token => !string.IsNullOrEmpty(token))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (tokens.Count > 0)
                await firebaseService.SendSecretMessageBatchAsync(
                    tokens, @event.MessageId, @event.SenderUserId, @event.SenderDeviceId.ToString(),
                    context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка secret message push. MessageId: {MessageId}", @event.MessageId);
        }
    }
}
