using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.Federation;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;
using BarkFluff.Shared.Queue.Messages;

using MassTransit;

using MediatR;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Message = BarkFluff.Messages.Domain.Message;
using MessageContent = BarkFluff.Messages.Domain.MessageContent;
using MessageContentType = BarkFluff.Messages.Domain.MessageContentType;

namespace BarkFluff.Messages.Features.ImportFederatedMessage;

// Применение входящего NewMessage (docs/rearch/05, шаги в step-2.3 Изменение 4):
// 1) чат неизвестен → RETRY:ChatUnknown (catch-up дотянет в 2.6);
// 2) чат Rejected/Merged → пока только Active (2.5/2.7);
// 3) идемпотентность (ChatId, FederatedId);
// 4) валидации: sender.uuid — remote-участник этого чата; clamp метки; лимиты контента;
// 5) вставка сообщения (SenderId = NULL, SenderUuid, LastChangeAt = origin_ts);
// 6) запись wire-байтов события в FederatedMessageEvents (для catch-up 2.6);
// 7) публикация обычного NewMessageEvent (+федеративные поля) → Updates/CloudMessaging работают штатно.
public class ImportFederatedMessageCommandHandler : IRequestHandler<ImportFederatedMessageCommand, ImportFederatedMessageResponse>
{
    private readonly MessagesContext _context;
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly MessageQueueSender _messageQueueSender;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<ImportFederatedMessageCommandHandler> _logger;

    public ImportFederatedMessageCommandHandler(
        MessagesContext context,
        MessagesStorage messagesStorage,
        ChatsStorage chatsStorage,
        MessageQueueSender messageQueueSender,
        IConfiguration configuration,
        MetricsCollector metrics,
        ILogger<ImportFederatedMessageCommandHandler> logger)
    {
        _context = context;
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _messageQueueSender = messageQueueSender;
        _configuration = configuration;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ImportFederatedMessageResponse> Handle(ImportFederatedMessageCommand command, CancellationToken cancellationToken)
    {
        var r = command.Request;

        if (!Guid.TryParse(r.ChatId, out var chatId))
            throw new ChatIdNotValidException();
        if (!Guid.TryParse(r.FederatedMessageId, out var federatedId))
            throw new ChatIdNotValidException();
        if (!Guid.TryParse(r.SenderUuid, out var senderUuid))
            throw new BarkFluff.Shared.Exceptions.Messages.MessageNotContainContextException();

        var originTs = FederationImportValidator.ClampOriginTs(r.OriginTsMs);

        // (1) чат существует.
        var chat = await _chatsStorage.GetFederatedChatAsync(chatId);
        if (chat is null)
        {
            _logger.LogDebug("ImportFederatedMessage: чат {ChatId} неизвестен → RETRY", chatId);
            throw new ChatUnknownException();
        }

        // (2) статус. В отличие от "чат ещё не синхронизирован" (RETRY выше) — Rejected/Merged это
        // перманентное состояние, ретраить бессмысленно.
        if (chat.FederatedStatus != FederatedStatus.Active)
        {
            // 2.5 (Rejected) / 2.7 (Merged) — пока принимаем только Active.
            _logger.LogWarning(
                "ImportFederatedMessage: чат {ChatId} имеет статус {Status}, сообщение отклонено",
                chatId, chat.FederatedStatus);
            throw new FederatedChatNotActiveException();
        }

        // (3) идемпотентность.
        var existing = await _context.Messages
            .Where(m => m.ChatId == chatId && m.FederatedId == federatedId)
            .Select(m => new { m.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return new ImportFederatedMessageResponse { MessageId = existing.Id };

        // (4) sender — remote-участник этого чата (payload-проверка уже сделана Federation по origin,
        // здесь сверяем с конкретным чатом).
        var remoteMember = chat.Members?.FirstOrDefault(m =>
            m.UserUuid == senderUuid && !string.IsNullOrEmpty(m.ServerName));
        if (remoteMember is null)
        {
            _logger.LogWarning(
                "ImportFederatedMessage: sender {SenderUuid} не remote-участник чата {ChatId}",
                senderUuid, chatId);
            throw new NoAccessToChatException();
        }

        // (4b) лимиты контента.
        FederationImportValidator.ValidateText(r.Text);
        FederationImportValidator.ValidateAttachmentCount(r.Attachments.Count);

        // (5) вставка сообщения. Вложения не рендерятся в этом этапе (Фаза 3.1); факт наличия сохраняем
        // через пустой список — ImportFederatedMessageRequest.attachments уже приехал, но пока без снапшота.
        var sentAt = originTs;
        var message = new Message
        {
            ChatId = chatId,
            SenderId = null,
            SenderUuid = senderUuid,
            FederatedId = federatedId,
            SentAt = sentAt,
            LastChangeAt = sentAt,
            Type = MessageContentType.Generic,
            ReadBy = new List<long>(),
            Content = new MessageContent
            {
                Text = r.Text,
                Attachments = new List<Domain.MessageAttachment>(),
            },
        };

        message = await _messagesStorage.AddMessage(message);

        // (6) FederatedMessageEvents — wire-байты подписанного FederationEvent (catch-up 2.6) + метка
        // (origin_server, event_id) для LWW tie-break последующих ApplyFederatedEdit/Delete (2.4).
        if (r.RawEvent is { Length: > 0 })
        {
            Guid.TryParse(r.EventId, out var eventId);
            _context.FederatedMessageEvents.Add(new FederatedMessageEvent
            {
                ChatId = chatId,
                FederatedId = federatedId,
                EventBytes = r.RawEvent.ToByteArray(),
                ReceivedAt = DateTime.UtcNow,
                OriginServer = remoteMember.ServerName ?? string.Empty,
                EventId = eventId,
            });
            await _context.SaveChangesAsync(cancellationToken);
        }

        // (7) публикация обычного NewMessageEvent для локальной рассылки (Updates/CloudMessaging).
        // ChatMembers — только локальные (remote получит по fed-каналу отправителя).
        var localMemberIds = chat.Members?.LocalUserIds() ?? new List<long>();

        var ownServer = _configuration["Federation:ServerName"] ?? string.Empty;
        await _messageQueueSender.SendImportedMessage(
            message,
            chatId,
            localMemberIds,
            senderUuid,
            r.SenderUsername,
            remoteMember.ServerName ?? string.Empty,
            ownServer);

        _metrics.Increment("federation_import_message_created");
        _logger.LogInformation(
            "ImportFederatedMessage: импортировано fed-сообщение {FederatedId} в чат {ChatId} от {SenderUuid}",
            federatedId, chatId, senderUuid);

        return new ImportFederatedMessageResponse { MessageId = message.Id };
    }
}
