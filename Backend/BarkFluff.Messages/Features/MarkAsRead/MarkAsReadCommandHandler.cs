using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Exceptions.Messages;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Messages.Features.MarkAsRead;

public class MarkAsReadCommandHandler : IRequestHandler<MarkAsReadCommand>
{
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly UserContext _userContext;
    private readonly ReadByQueueSender _readByQueueSender;
    private readonly ILogger<MarkAsReadCommandHandler> _logger;

    public MarkAsReadCommandHandler(
        MessagesStorage messagesStorage,
        ChatsStorage chatsStorage,
        UserContext userContext, ReadByQueueSender readByQueueSender,
        ILogger<MarkAsReadCommandHandler> logger)
    {
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _userContext = userContext;
        _readByQueueSender = readByQueueSender;
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

        // Проверяем доступ к каждому чату
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
        }

        // Обновляем ReadBy для сообщений
        await _messagesStorage.MarkMessagesAsRead(request.MessageIds, _userContext.UserId);

        _logger.LogDebug("Отправка событий о прочтении в очередь для {MessageCount} сообщений", messages.Count);

        foreach (var message in messages)
        {
            if (!message.ReadBy.Contains(_userContext.UserId))
            {
                message.ReadBy.Add(_userContext.UserId);
            }

            var chatMembers = await _chatsStorage.GetChatMembers(message.ChatId, 0, int.MaxValue);

            await _readByQueueSender.SendEvent(message.ChatId, message.Id, message.ReadBy, chatMembers.Select(x => x.UserId).ToList());
        }

        _logger.LogInformation(
            "Успешно отмечено {MessageCount} сообщений как прочитанные пользователем {UserId}",
            messages.Count,
            _userContext.UserId
        );
    }
} 