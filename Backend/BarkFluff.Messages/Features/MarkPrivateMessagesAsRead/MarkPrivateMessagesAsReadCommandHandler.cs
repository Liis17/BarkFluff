using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.MarkPrivateMessagesAsRead;

public class MarkPrivateMessagesAsReadCommandHandler : IRequestHandler<MarkPrivateMessagesAsReadCommand>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly EncryptedMessagesStorage _messagesStorage;
    private readonly EncryptedMessageQueueSender _queueSender;
    private readonly UserContext _userContext;

    public MarkPrivateMessagesAsReadCommandHandler(
        ChatsStorage chatsStorage,
        EncryptedMessagesStorage messagesStorage,
        EncryptedMessageQueueSender queueSender,
        UserContext userContext)
    {
        _chatsStorage = chatsStorage;
        _messagesStorage = messagesStorage;
        _queueSender = queueSender;
        _userContext = userContext;
    }

    public async Task Handle(MarkPrivateMessagesAsReadCommand request, CancellationToken cancellationToken)
    {
        if (request.LastReadMessageId <= 0)
        {
            return;
        }

        var chat = await _chatsStorage.GetChat(request.ChatId);
        if (chat is null)
        {
            throw new ChatNotFoundException();
        }

        if (chat.Type != ChatType.Private)
        {
            throw new ChatNotPrivateException();
        }

        if (chat.Members?.All(member => member.UserId != _userContext.UserId) ?? true)
        {
            throw new NoAccessToChatException();
        }

        var lastReadId = await _messagesStorage.MarkReadThroughAsync(
            request.ChatId,
            _userContext.UserId,
            request.LastReadMessageId);

        if (lastReadId > 0)
        {
            await _queueSender.SendReadState(
                request.ChatId,
                _userContext.UserId,
                lastReadId,
                chat.Members!.Select(member => member.UserId).ToList());
        }
    }
}
