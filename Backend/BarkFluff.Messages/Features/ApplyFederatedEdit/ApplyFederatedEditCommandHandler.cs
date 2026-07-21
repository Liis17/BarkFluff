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

namespace BarkFluff.Messages.Features.ApplyFederatedEdit;

// Применение входящей правки (docs/rearch/05, docs/rearch/phase-2/step-2.4-edit-delete-read-lww.md,
// Изменение 3):
// 1) чат неизвестен → RETRY:ChatUnknown; сообщение по FederatedId неизвестно → RETRY:MessageUnknown;
// 2) P2-02: домашняя нода автора правимого сообщения обязана совпадать с origin события —
//    проверяется локально по ChatMember, а не по полю в payload (payload не содержит identity автора);
// 3) LWW (LwwResolver): новее — применить; старше/после удаления — игнорировать (ответ OK);
// 4) обновление FederatedMessageEvents (событие-победитель заменяет предыдущее);
// 5) публикация MessageEditedEvent для локальной рассылки (Updates) — без re-federation (пришло оттуда).
public class ApplyFederatedEditCommandHandler : IRequestHandler<ApplyFederatedEditCommand, ApplyFederatedEditResponse>
{
    private readonly MessagesContext _context;
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<ApplyFederatedEditCommandHandler> _logger;

    public ApplyFederatedEditCommandHandler(
        MessagesContext context,
        MessagesStorage messagesStorage,
        ChatsStorage chatsStorage,
        MessageQueueSender messageQueueSender,
        IConfiguration configuration,
        MetricsCollector metrics,
        ILogger<ApplyFederatedEditCommandHandler> logger)
    {
        _context = context;
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _messageQueueSender = messageQueueSender;
        _configuration = configuration;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ApplyFederatedEditResponse> Handle(ApplyFederatedEditCommand command, CancellationToken cancellationToken)
    {
        var r = command.Request;

        if (!Guid.TryParse(r.ChatId, out var chatId))
            throw new ChatIdNotValidException();
        if (!Guid.TryParse(r.FederatedMessageId, out var federatedId))
            throw new ChatIdNotValidException();

        var originTs = FederationImportValidator.ClampOriginTs(r.OriginTsMs);
        Guid.TryParse(r.EventId, out var incomingEventId);
        FederationImportValidator.ValidateText(r.NewText);

        var chat = await _chatsStorage.GetFederatedChatAsync(chatId);
        if (chat is null || chat.FederatedStatus != FederatedStatus.Active)
        {
            _logger.LogDebug("ApplyFederatedEdit: чат {ChatId} неизвестен → RETRY", chatId);
            throw new ChatUnknownException();
        }

        var message = await _messagesStorage.GetByFederatedIdAsync(chatId, federatedId);
        if (message is null)
        {
            _logger.LogDebug("ApplyFederatedEdit: сообщение {FederatedId} неизвестно → RETRY", federatedId);
            throw new FederatedMessageUnknownException();
        }

        // P2-02: нода говорит только за своих — резолвим домашнюю ноду автора ПРАВИМОГО сообщения
        // локально (по участникам чата), а не по полю payload'а — там identity автора намеренно нет.
        var ownServer = _configuration["Federation:ServerName"] ?? string.Empty;
        var homeServer = message.SenderUuid.HasValue
            ? FederationImportValidator.ResolveHomeServer(chat, message.SenderUuid.Value, ownServer)
            : null;
        if (homeServer is null || !string.Equals(homeServer, r.OriginServer, StringComparison.OrdinalIgnoreCase))
        {
            _metrics.Increment("events_rejected.author_not_origin");
            _logger.LogWarning(
                "ApplyFederatedEdit: origin {Origin} не домашняя нода автора сообщения {FederatedId}",
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
            _metrics.Increment("federation_apply_edit_stale");
            return new ApplyFederatedEditResponse { Applied = false };
        }

        message.Content ??= new MessageContent();
        message.Content.Text = r.NewText;
        message.IsEdited = true;
        message.EditedAt = originTs;
        message.LastChangeAt = originTs;
        await _messagesStorage.SaveChangesAsync();

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
        await _messageQueueSender.SendEdited(
            message,
            chatId,
            localMemberIds,
            filesInfoMap: null,
            isFederated: true,
            federatedId: federatedId,
            senderUuid: message.SenderUuid,
            remoteParticipants: new List<FederatedParticipant>(),
            lastChangeAt: originTs);

        _metrics.Increment("federation_apply_edit_applied");
        _logger.LogInformation("ApplyFederatedEdit: применена правка {FederatedId} в чате {ChatId}", federatedId, chatId);

        return new ApplyFederatedEditResponse { Applied = true };
    }
}
