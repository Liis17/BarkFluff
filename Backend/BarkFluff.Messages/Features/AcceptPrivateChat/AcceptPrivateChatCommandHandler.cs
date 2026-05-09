using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.AcceptPrivateChat;

public class AcceptPrivateChatCommandHandler : IRequestHandler<AcceptPrivateChatCommand, AcceptPrivateChatResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly PrivateChatInviteStore _inviteStore;
    private readonly EncryptedMessageQueueSender _queueSender;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<AcceptPrivateChatCommandHandler> _logger;

    public AcceptPrivateChatCommandHandler(
        ChatsStorage chatsStorage,
        PrivateChatInviteStore inviteStore,
        EncryptedMessageQueueSender queueSender,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<AcceptPrivateChatCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _inviteStore = inviteStore;
        _queueSender = queueSender;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<AcceptPrivateChatResponse> Handle(AcceptPrivateChatCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Пользователь {UserId} принимает приватный чат {ChatId}",
            _userContext.UserId, request.ChatId);

        var chat = await _chatsStorage.GetChat(request.ChatId);

        if (chat is null)
        {
            throw new ChatNotFoundException();
        }

        if (chat.Type != ChatType.Private)
        {
            throw new ChatNotPrivateException();
        }

        var invitee = await _inviteStore.GetInviteeAsync(request.ChatId);
        if (invitee is null)
        {
            throw new PrivateChatInviteNotFoundException();
        }

        if (invitee.Value != _userContext.UserId)
        {
            throw new NoAccessToChatException();
        }

        if (chat.Members?.Any(m => m.UserId == _userContext.UserId) == true)
        {
            throw new PrivateChatAlreadyAcceptedException();
        }

        await _chatsStorage.AddChatMember(request.ChatId, _userContext.UserId);

        await _inviteStore.RemoveAsync(request.ChatId);

        var inviterId = chat.Members?.FirstOrDefault()?.UserId ?? 0;
        await _queueSender.SendInviteResolution(
            request.ChatId,
            inviterId,
            _userContext.UserId,
            accepted: true);

        _metrics.Increment("private_chats_accepted");

        var updated = await _chatsStorage.GetChat(request.ChatId);
        return new AcceptPrivateChatResponse { Chat = updated!.ToGrpc() };
    }
}
