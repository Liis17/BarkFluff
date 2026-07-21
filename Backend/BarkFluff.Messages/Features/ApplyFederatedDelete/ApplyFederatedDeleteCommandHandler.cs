using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.Federation;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;
using BarkFluff.Shared.Queue.Federation;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Messages.Features.ApplyFederatedDelete;

// Применение входящего удаления (docs/rearch/05, step-2.4, Изменение 3) — симметрично
// ApplyFederatedEdit: неизвестный чат/сообщение → RETRY; P2-02 origin-проверка; LWW; обновление
// FederatedMessageEvents; локальная рассылка MessageDeletedEvent (+снятие локального pin, если был).
public class ApplyFederatedDeleteCommandHandler : IRequestHandler<ApplyFederatedDeleteCommand, ApplyFederatedDeleteResponse>
{
    private readonly MessagesContext _context;
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly PinnedMessagesStorage _pinnedMessagesStorage;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<ApplyFederatedDeleteCommandHandler> _logger;

    public ApplyFederatedDeleteCommandHandler(
        MessagesContext context,
        MessagesStorage messagesStorage,
        ChatsStorage chatsStorage,
        PinnedMessagesStorage pinnedMessagesStorage,
        MessageQueueSender messageQueueSender,
        IConfiguration configuration,
        MetricsCollector metrics,
        ILogger<ApplyFederatedDeleteCommandHandler> logger)
    {
        _context = context;
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _pinnedMessagesStorage = pinnedMessagesStorage;
        _messageQueueSender = messageQueueSender;
        _configuration = configuration;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ApplyFederatedDeleteResponse> Handle(ApplyFederatedDeleteCommand command, CancellationToken cancellationToken)
    {
        var r = command.Request;

        if (!Guid.TryParse(r.ChatId, out var chatId))
            throw new ChatIdNotValidException();
        if (!Guid.TryParse(r.FederatedMessageId, out var federatedId))
            throw new ChatIdNotValidException();

        var originTs = FederationImportValidator.ClampOriginTs(r.OriginTsMs);
        Guid.TryParse(r.EventId, out var incomingEventId);

        var chat = await _chatsStorage.GetFederatedChatAsync(chatId);
        if (chat is null)
        {
            _logger.LogDebug("ApplyFederatedDelete: чат {ChatId} неизвестен → RETRY", chatId);
            throw new ChatUnknownException();
        }

        // Rejected/Merged — перманентное состояние (в отличие от "ещё не синхронизирован" выше),
        // ретраить бессмысленно.
        if (chat.FederatedStatus != FederatedStatus.Active)
        {
            _logger.LogDebug("ApplyFederatedDelete: чат {ChatId} не активен (статус {Status})", chatId, chat.FederatedStatus);
            throw new FederatedChatNotActiveException();
        }

        var message = await _messagesStorage.GetByFederatedIdAsync(chatId, federatedId);
        if (message is null)
        {
            _logger.LogDebug("ApplyFederatedDelete: сообщение {FederatedId} неизвестно → RETRY", federatedId);
            throw new FederatedMessageUnknownException();
        }

        var ownServer = _configuration["Federation:ServerName"] ?? string.Empty;
        var homeServer = message.SenderUuid.HasValue
            ? FederationImportValidator.ResolveHomeServer(chat, message.SenderUuid.Value, ownServer)
            : null;
        if (homeServer is null || !string.Equals(homeServer, r.OriginServer, StringComparison.OrdinalIgnoreCase))
        {
            _metrics.Increment("events_rejected.author_not_origin");
            _logger.LogWarning(
                "ApplyFederatedDelete: origin {Origin} не домашняя нода автора сообщения {FederatedId}",
                r.OriginServer, federatedId);
            throw new FederatedOriginMismatchException();
        }

        var existingEvent = await _context.FederatedMessageEvents
            .FirstOrDefaultAsync(e => e.ChatId == chatId && e.FederatedId == federatedId, cancellationToken);

        var shouldApply = LwwResolver.ShouldApplyMessageChange(
            currentIsDeleted: message.IsDeleted,
            currentLastChangeAt: message.LastChangeAt,
            currentOriginServer: existingEvent?.OriginServer ?? string.Empty,
            currentEventId: existingEvent?.EventId ?? Guid.Empty,
            incomingOriginTs: originTs,
            incomingOriginServer: r.OriginServer,
            incomingEventId: incomingEventId);

        if (!shouldApply)
        {
            _metrics.Increment("federation_apply_delete_stale");
            return new ApplyFederatedDeleteResponse { Applied = false };
        }

        message.IsDeleted = true;
        message.LastChangeAt = originTs;
        await _messagesStorage.SaveChangesAsync();

        var removedPin = await _pinnedMessagesStorage.RemoveByMessageIdAsync(message.Id);
        if (removedPin is not null)
            await _pinnedMessagesStorage.SaveChangesAsync();

        if (existingEvent is not null)
        {
            existingEvent.OriginServer = r.OriginServer;
            existingEvent.EventId = incomingEventId;
            existingEvent.ReceivedAt = DateTime.UtcNow;
            if (r.RawEvent is { Length: > 0 })
                existingEvent.EventBytes = r.RawEvent.ToByteArray();
        }
        else
        {
            _context.FederatedMessageEvents.Add(new Domain.FederatedMessageEvent
            {
                ChatId = chatId,
                FederatedId = federatedId,
                EventBytes = r.RawEvent is { Length: > 0 } ? r.RawEvent.ToByteArray() : [],
                ReceivedAt = DateTime.UtcNow,
                OriginServer = r.OriginServer,
                EventId = incomingEventId,
            });
        }
        await _context.SaveChangesAsync(cancellationToken);

        var localMemberIds = chat.Members?.LocalUserIds() ?? new List<long>();
        await _messageQueueSender.SendDeleted(
            chatId,
            message.Id,
            localMemberIds,
            isFederated: true,
            federatedId: federatedId,
            remoteParticipants: new List<FederatedParticipant>(),
            lastChangeAt: originTs);

        if (removedPin is not null)
            await _messageQueueSender.SendUnpinned(chatId, message.Id, localMemberIds);

        _metrics.Increment("federation_apply_delete_applied");
        _logger.LogInformation("ApplyFederatedDelete: применено удаление {FederatedId} в чате {ChatId}", federatedId, chatId);

        return new ApplyFederatedDeleteResponse { Applied = true };
    }
}
