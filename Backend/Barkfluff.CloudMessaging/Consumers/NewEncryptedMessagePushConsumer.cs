using Barkfluff.CloudMessaging.Services;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Messages;
using MassTransit;

namespace Barkfluff.CloudMessaging.Consumers;

public class NewEncryptedMessagePushConsumer(
    UsersServerApi.UsersServerApiClient usersClient,
    FirebaseService firebaseService,
    ILogger<NewEncryptedMessagePushConsumer> logger) : IConsumer<NewEncryptedMessageEvent>
{
    public async Task Consume(ConsumeContext<NewEncryptedMessageEvent> context)
    {
        var @event = context.Message;
        try
        {
            var message = EncryptedMessage.Parser.ParseFrom(@event.Message);
            var recipients = @event.ChatMembers.Where(id => id != message.SenderId).Distinct().ToList();
            if (recipients.Count == 0)
                return;

            var response = await usersClient.GetDevicesWithFirebaseTokensAsync(
                new GetDevicesWithFirebaseTokensRequest
                {
                    UserIds = { recipients },
                    ChatId = @event.ChatId.ToString()
                }, cancellationToken: context.CancellationToken);
            var tokens = response.Tokens
                .Where(token => token.PushPlatform != PushPlatform.Web && recipients.Contains(token.UserId))
                .Select(token => token.FirebaseToken)
                .Where(token => !string.IsNullOrEmpty(token))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (tokens.Count > 0)
                await firebaseService.SendPrivateMessageBatchAsync(
                    tokens, @event.ChatId.ToString(), message.Id, message.SenderId, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка private message push. ChatId: {ChatId}", @event.ChatId);
        }
    }
}
