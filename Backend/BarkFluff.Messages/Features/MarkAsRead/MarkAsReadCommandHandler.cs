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

    public MarkAsReadCommandHandler(
        MessagesStorage messagesStorage,
        ChatsStorage chatsStorage,
        UserContext userContext, ReadByQueueSender readByQueueSender)
    {
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _userContext = userContext;
        _readByQueueSender = readByQueueSender;
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

        foreach (var message in messages)
        {
            if (!message.ReadBy.Contains(_userContext.UserId))
            { 
                message.ReadBy.Add(_userContext.UserId);
            }

            var chatMembers = await _chatsStorage.GetChatMembers(message.ChatId, 0, int.MaxValue);
            
            await _readByQueueSender.SendEvent(message.ChatId, message.Id, message.ReadBy, chatMembers.Select(x => x.UserId).ToList());
        }
    }
} 