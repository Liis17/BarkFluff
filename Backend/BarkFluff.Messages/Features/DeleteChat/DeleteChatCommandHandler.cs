using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.DeleteChat;

public class DeleteChatCommandHandler : IRequestHandler<DeleteChatCommand, DeleteChatResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly MessagesStorage _messagesStorage;
    private readonly EncryptedMessagesStorage _encryptedMessagesStorage;
    private readonly PinnedMessagesStorage _pinnedMessagesStorage;
    private readonly UserContext _userContext;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<DeleteChatCommandHandler> _logger;

    public DeleteChatCommandHandler(ChatsStorage chatsStorage, MessagesStorage messagesStorage,
        EncryptedMessagesStorage encryptedMessagesStorage, PinnedMessagesStorage pinnedMessagesStorage,
        UserContext userContext, MessageQueueSender messageQueueSender, MetricsCollector metrics,
        ILogger<DeleteChatCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _messagesStorage = messagesStorage;
        _encryptedMessagesStorage = encryptedMessagesStorage;
        _pinnedMessagesStorage = pinnedMessagesStorage;
        _userContext = userContext;
        _messageQueueSender = messageQueueSender;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<DeleteChatResponse> Handle(DeleteChatCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Удаление чата {ChatId} пользователем {UserId} (DeleteForEveryone={DeleteForEveryone})",
            request.ChatId, _userContext.UserId, request.DeleteForEveryone
        );

        var hasAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

        if (!hasAccess)
        {
            throw new NoAccessToChatException();
        }

        var chat = await _chatsStorage.GetChat(request.ChatId);

        if (chat == null)
        {
            throw new NoAccessToChatException();
        }

        if (chat.IsGroupChat)
        {
            // Групповые чаты удаляются через выход из группы (LeaveChat) — у DeleteChat
            // нет для них смысла "удалить для всех".
            throw new ChatTypeMismatchException();
        }

        // Федеративные DM: нет протокола, чтобы синхронно стереть копию на чужой ноде —
        // клиент заранее скрывает чекбокс, здесь вторая линия защиты, чтобы не соврать
        // пользователю о судьбе переписки на стороне собеседника.
        var effectiveWipe = request.DeleteForEveryone && !chat.IsFederated;

        var hideUserIds = effectiveWipe
            ? chat.Members!.LocalUserIds()
            : [_userContext.UserId];

        var hiddenAt = DateTime.UtcNow;

        await _chatsStorage.HideChatForUsers(request.ChatId, hideUserIds, hiddenAt);

        if (effectiveWipe)
        {
            if (chat.Type == ChatType.Private)
            {
                await _encryptedMessagesStorage.SoftDeleteAllInChatAsync(request.ChatId);
            }
            else
            {
                await _messagesStorage.SoftDeleteAllInChatAsync(request.ChatId);

                var unpinnedCount = await _pinnedMessagesStorage.RemoveAllByChatAsync(request.ChatId);
                if (unpinnedCount > 0)
                {
                    await _pinnedMessagesStorage.SaveChangesAsync();
                    await _messageQueueSender.SendAllUnpinned(request.ChatId, hideUserIds);
                }
            }

            _metrics.Increment("chats_deleted_for_everyone");
        }

        await _messageQueueSender.SendChatHidden(request.ChatId, hideUserIds, effectiveWipe);

        _metrics.Increment("chats_deleted");

        _logger.LogInformation(
            "Чат {ChatId} удалён пользователем {UserId} (EffectiveWipe={EffectiveWipe})",
            request.ChatId, _userContext.UserId, effectiveWipe
        );

        return new DeleteChatResponse();
    }
}
