namespace BarkFluff.Messages.Consumers;

using BarkFluff.GrpcServer.Metrics;

using MassTransit;

using Microsoft.Extensions.Logging;

using Persistence.Services;

using Shared.Queue.Users;

public class UserChangedAvatarConsumer : IConsumer<UserChangedAvatar>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly ChatCache _chatCache;
    private readonly ILogger<UserChangedAvatarConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public UserChangedAvatarConsumer(ChatsStorage chatsStorage, ChatCache chatCache, ILogger<UserChangedAvatarConsumer> logger, MetricsCollector metrics)
    {
        _chatsStorage = chatsStorage;
        _chatCache = chatCache;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<UserChangedAvatar> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        var userId = context.Message.UserId;
        var profilePictureUrl = context.Message.ProfilePictureUrl;

        _logger.LogInformation(
            "Получено событие изменения аватара пользователя {UserId}: '{ProfilePictureUrl}'",
            userId,
            profilePictureUrl
        );

        try
        {
            var chatsWithUser = await _chatsStorage.GetDmChatsWithUser(userId);

            _logger.LogDebug(
                "Найдено {ChatCount} личных чатов для обновления аватара пользователя {UserId}",
                chatsWithUser.Count,
                userId
            );

            foreach (var chat in chatsWithUser)
            {
                var personDm = chat.Members![0].UserId == userId ? chat.Members[1].UserId : chat.Members[0].UserId;

                await _chatCache.SetChatImage(chat.Id, personDm, profilePictureUrl);
            }

            _logger.LogInformation(
                "Успешно обновлен аватар пользователя {UserId} в {ChatCount} чатах",
                userId,
                chatsWithUser.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Ошибка при обработке изменения аватара пользователя {UserId}",
                userId
            );
            throw;
        }
    }
}