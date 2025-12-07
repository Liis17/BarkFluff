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
    private readonly MessageQueueSender _messageQueueSender;

    public MarkAsReadCommandHandler(
        MessagesStorage messagesStorage,
        ChatsStorage chatsStorage,
        UserContext userContext,
        MessageQueueSender messageQueueSender)
    {
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _userContext = userContext;
        _messageQueueSender = messageQueueSender;
    }

    public async Task Handle(MarkAsReadCommand request, CancellationToken cancellationToken)
    {
        if (!request.MessageIds.Any())
        {
            return;
        }

        // Получаем все сообщения
        var messages = await _messagesStorage.GetMessagesByIds(request.MessageIds);

        if (!messages.Any())
        {
            return;
        }

        // Получаем уникальные ID чатов из сообщений
        var chatIds = messages.Select(m => m.ChatId).Distinct().ToList();

        // Проверяем доступ к каждому чату
        foreach (var chatId in chatIds)
        {
            var hasAccess = await _chatsStorage.CheckAccessToChat(chatId, _userContext.UserId);
            if (!hasAccess)
            {
                throw new NoAccessToChatException();
            }
        }

        // Обновляем ReadBy для сообщений
        await _messagesStorage.MarkMessagesAsRead(request.MessageIds, _userContext.UserId);

        // Получаем обновлённые сообщения одним запросом вместо N запросов
        var updatedMessages = await _messagesStorage.GetMessagesByIds(request.MessageIds);

        // Публикуем события об обновлении статуса прочтения
        foreach (var chatId in chatIds)
        {
            var chatMessages = messages.Where(m => m.ChatId == chatId).ToList();
            var chat = await _chatsStorage.GetChat(chatId);
            
            if (chat == null)
            {
                continue;
            }
            
            var chatMembers = chat.Members.Select(m => m.UserId).ToList();

            // Получаем последнее сообщение в чате для определения IsLastMessage
            var lastMessage = await _messagesStorage.GetLastMessageInChat(chatId);

            foreach (var message in chatMessages)
            {
                // Находим обновлённое сообщение в списке
                var updatedMessage = updatedMessages.FirstOrDefault(m => m.Id == message.Id);
                
                if (updatedMessage == null)
                {
                    continue;
                }
                
                var isLastMessage = lastMessage != null && lastMessage.Id == message.Id;

                await _messageQueueSender.SendReadReceipt(
                    chatId,
                    message.Id,
                    updatedMessage.ReadBy,
                    chatMembers,
                    isLastMessage);
            }
        }
    }
} 