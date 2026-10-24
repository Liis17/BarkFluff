using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.MarkAsRead;

public sealed class MarkChatAsReadUpToCommandHandler : IRequestHandler<MarkChatAsReadUpToCommand, bool>
{
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;

    public MarkChatAsReadUpToCommandHandler(
        MessagesStorage messagesStorage,
        ChatsStorage chatsStorage,
        UserContext userContext,
        MetricsCollector metrics)
    {
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _userContext = userContext;
        _metrics = metrics;
    }

    public async Task<bool> Handle(MarkChatAsReadUpToCommand request, CancellationToken cancellationToken)
    {
        if (request.UpToMessageId <= 0) throw new MessageNotFoundException();

        var chat = await _chatsStorage.GetChat(request.ChatId, cancellationToken)
            ?? throw new ChatNotFoundException();
        if (chat.Members!.All(member => member.UserId != _userContext.UserId))
            throw new NoAccessToChatException();
        if (chat.Type != ChatType.Regular) throw new ChatTypeMismatchException();

        var anchor = await _messagesStorage.GetMessageById(request.UpToMessageId, cancellationToken);
        if (anchor is null || anchor.ChatId != request.ChatId) throw new MessageNotFoundException();

        var (changedCount, hasMore) = await _messagesStorage.MarkChatMessagesAsReadUpToWithOutboxAsync(
            request.ChatId,
            request.UpToMessageId,
            _userContext.UserId,
            cancellationToken);
        _metrics.Add("messages_read_range_changed", changedCount);
        _metrics.Increment("messages_read_range_calls");
        return hasMore;
    }
}
