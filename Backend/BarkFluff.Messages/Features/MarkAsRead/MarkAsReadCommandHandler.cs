using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.MarkAsRead;

public class MarkAsReadCommandHandler : IRequestHandler<MarkAsReadCommand>
{
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly UserContext _userContext;
    private readonly ReadByQueueSender _readByQueueSender;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<MarkAsReadCommandHandler> _logger;

    public MarkAsReadCommandHandler(
        MessagesStorage messagesStorage,
        ChatsStorage chatsStorage,
        UserContext userContext, ReadByQueueSender readByQueueSender,
        MetricsCollector metrics,
        ILogger<MarkAsReadCommandHandler> logger)
    {
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _userContext = userContext;
        _readByQueueSender = readByQueueSender;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task Handle(MarkAsReadCommand request, CancellationToken cancellationToken)
    {
        if (!request.MessageIds.Any())
        {
            _logger.LogDebug("Пустой список ID сообщений для отметки как прочитанные");
            return;
        }

        _logger.LogInformation(
            "Отметка {MessageCount} сообщений как прочитанных пользователем {UserId}",
            request.MessageIds.Count(),
            _userContext.UserId
        );

        // Получаем все сообщения
        var messages = await _messagesStorage.GetMessagesByIds(request.MessageIds);

        if (!messages.Any())
        {
            _logger.LogWarning("Ни одно из сообщений {MessageIds} не найдено", string.Join(", ", request.MessageIds));
            return;
        }

        _logger.LogDebug("Получено {ActualCount} сообщений из {RequestedCount}", messages.Count, request.MessageIds.Count());

        // Получаем уникальные ID чатов из сообщений
        var chatIds = messages.Select(m => m.ChatId).Distinct().ToList();

        _logger.LogDebug("Проверка доступа к {ChatCount} чатам", chatIds.Count);

        // Кэш участников чатов для оптимизации - получаем один раз для каждого чата
        var chatMembersCache = new Dictionary<Guid, List<long>>();

        // Проверяем доступ к каждому чату и кэшируем участников
        foreach (var chatId in chatIds)
        {
            var hasAccess = await _chatsStorage.CheckAccessToChat(chatId, _userContext.UserId);
            if (!hasAccess)
            {
                _logger.LogWarning(
                    "Пользователь {UserId} не имеет доступа к чату {ChatId}",
                    _userContext.UserId,
                    chatId
                );
                throw new NoAccessToChatException();
            }

            // Получаем участников чата один раз и кэшируем
            var chatMembers = await _chatsStorage.GetChatMembers(chatId, 0, int.MaxValue);
            chatMembersCache[chatId] = chatMembers.Select(x => x.UserId).ToList();
        }

        var newlyReadMessages = messages
            .Where(message => !message.ReadBy.Contains(_userContext.UserId))
            .ToList();

        if (newlyReadMessages.Count == 0)
        {
            _logger.LogDebug(
                "Все запрошенные сообщения уже прочитаны пользователем {UserId}",
                _userContext.UserId);
            return;
        }

        // Обновляем ReadBy только для сообщений, которые пользователь ещё не читал.
        await _messagesStorage.MarkMessagesAsRead(
            newlyReadMessages.Select(message => message.Id).ToList(),
            _userContext.UserId);

        _logger.LogDebug(
            "Отправка событий о прочтении в очередь для {MessageCount} сообщений",
            newlyReadMessages.Count);

        // Отправляем события параллельно для ускорения
        var sendTasks = new List<Task>();
        foreach (var message in newlyReadMessages)
        {
            // Используем кэшированных участников чата
            var chatMembers = chatMembersCache[message.ChatId];

            sendTasks.Add(_readByQueueSender.SendEvent(
                message.ChatId,
                message.Id,
                [_userContext.UserId],
                chatMembers));
        }

        await Task.WhenAll(sendTasks);

        _metrics.Add("messages_marked_as_read", newlyReadMessages.Count);

        _logger.LogInformation(
            "Успешно отмечено {MessageCount} сообщений как прочитанные пользователем {UserId}",
            newlyReadMessages.Count,
            _userContext.UserId
        );
    }
}
