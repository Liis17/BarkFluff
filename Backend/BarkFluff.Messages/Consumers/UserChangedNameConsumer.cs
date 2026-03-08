namespace BarkFluff.Messages.Consumers;

using BarkFluff.GrpcServer.Metrics;

using MassTransit;

using Microsoft.Extensions.Logging;

using Persistence.Services;

using Shared.Queue.Users;

public class UserChangedNameConsumer : IConsumer<UserChangedName>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly ChatCache _chatCache;
    private readonly ILogger<UserChangedNameConsumer> _logger;
    private readonly MetricsCollector _metrics;

    public UserChangedNameConsumer(ChatsStorage chatsStorage, ChatCache chatCache, ILogger<UserChangedNameConsumer> logger, MetricsCollector metrics)
    {
        _chatsStorage = chatsStorage;
        _chatCache = chatCache;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task Consume(ConsumeContext<UserChangedName> context)
    {
        _metrics.Increment("rabbitmq_events_consumed");
        var userId = context.Message.UserId;
        var newName = $"{context.Message.NewFirstName} {context.Message.NewLastName}";

        _logger.LogInformation(
            "Получено событие изменения имени пользователя {UserId}: '{FirstName} {LastName}'",
            userId,
            context.Message.NewFirstName,
            context.Message.NewLastName
        );

        try
        {
            var chatsWithUser = await _chatsStorage.GetDmChatsWithUser(userId);

            _logger.LogDebug(
                "Найдено {ChatCount} личных чатов для обновления имени пользователя {UserId}",
                chatsWithUser.Count,
                userId
            );

            foreach (var chat in chatsWithUser)
            {
                var personDm = chat.Members![0].UserId == userId ? chat.Members[1].UserId : chat.Members[0].UserId;

                await _chatCache.SetChatName(chat.Id, personDm, newName);
            }

            _logger.LogInformation(
                "Успешно обновлено имя пользователя {UserId} в {ChatCount} чатах",
                userId,
                chatsWithUser.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Ошибка при обработке изменения имени пользователя {UserId}",
                userId
            );
            throw;
        }
    }
}