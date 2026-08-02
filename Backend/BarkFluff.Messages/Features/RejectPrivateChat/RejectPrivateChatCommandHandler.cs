using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.RejectPrivateChat;

public class RejectPrivateChatCommandHandler : IRequestHandler<RejectPrivateChatCommand, RejectPrivateChatResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly PrivateChatInviteStore _inviteStore;
    private readonly EncryptedMessageQueueSender _queueSender;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<RejectPrivateChatCommandHandler> _logger;

    public RejectPrivateChatCommandHandler(
        ChatsStorage chatsStorage,
        PrivateChatInviteStore inviteStore,
        EncryptedMessageQueueSender queueSender,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<RejectPrivateChatCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _inviteStore = inviteStore;
        _queueSender = queueSender;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<RejectPrivateChatResponse> Handle(RejectPrivateChatCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Пользователь {UserId} отклоняет приватный чат {ChatId}",
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

        var inviteeId = await _inviteStore.GetInviteeAsync(request.ChatId);
        if (inviteeId is null)
        {
            throw new PrivateChatInviteNotFoundException();
        }

        if (inviteeId != _userContext.UserId)
        {
            throw new NoAccessToChatException();
        }

        var inviterId = chat.Members?.FirstOrDefault()?.UserId ?? 0;

        await _inviteStore.RemoveAsync(request.ChatId);
        await _chatsStorage.RejectPrivateChat(request.ChatId);

        await _queueSender.SendInviteResolution(
            request.ChatId,
            inviterId,
            _userContext.UserId,
            accepted: false);

        _metrics.Increment("private_chats_rejected");

        return new RejectPrivateChatResponse();
    }
}
