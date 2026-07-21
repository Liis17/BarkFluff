using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.Federation;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;
using BarkFluff.Shared.Queue.Federation;

using MediatR;

using Microsoft.Extensions.Configuration;

namespace BarkFluff.Messages.Features.ApplyFederatedRead;

// Применение входящей отметки "прочитано" (docs/rearch/05, step-2.4, Изменение 4):
// 1) чат неизвестен → RETRY:ChatUnknown;
// 2) reader_uuid — remote-участник ЭТОГО чата, и его домашняя нода == origin события (P2-02);
// 3) upsert FederatedReadStates по "прочитано до" (идемпотентно, монотонно — LwwResolver.ShouldApplyRead);
// 4) внутреннее MessageReadEvent → Updates (только если локальная копия сообщения уже есть; иначе
//    прочтение всё равно сохранено и будет учтено при выдаче после catch-up 2.6).
public class ApplyFederatedReadCommandHandler : IRequestHandler<ApplyFederatedReadCommand, ApplyFederatedReadResponse>
{
    private readonly MessagesStorage _messagesStorage;
    private readonly ChatsStorage _chatsStorage;
    private readonly FederatedReadStatesStorage _federatedReadStatesStorage;
    private readonly ReadByQueueSender _readByQueueSender;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<ApplyFederatedReadCommandHandler> _logger;

    public ApplyFederatedReadCommandHandler(
        MessagesStorage messagesStorage,
        ChatsStorage chatsStorage,
        FederatedReadStatesStorage federatedReadStatesStorage,
        ReadByQueueSender readByQueueSender,
        IConfiguration configuration,
        MetricsCollector metrics,
        ILogger<ApplyFederatedReadCommandHandler> logger)
    {
        _messagesStorage = messagesStorage;
        _chatsStorage = chatsStorage;
        _federatedReadStatesStorage = federatedReadStatesStorage;
        _readByQueueSender = readByQueueSender;
        _configuration = configuration;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ApplyFederatedReadResponse> Handle(ApplyFederatedReadCommand command, CancellationToken cancellationToken)
    {
        var r = command.Request;

        if (!Guid.TryParse(r.ChatId, out var chatId))
            throw new ChatIdNotValidException();
        if (!Guid.TryParse(r.ReaderUuid, out var readerUuid))
            throw new ChatIdNotValidException();

        var originTs = FederationImportValidator.ClampOriginTs(r.OriginTsMs);
        Guid? upToFederatedId = Guid.TryParse(r.UpToFederatedMessageId, out var upTo) ? upTo : null;

        var chat = await _chatsStorage.GetFederatedChatAsync(chatId);
        if (chat is null || chat.FederatedStatus != FederatedStatus.Active)
        {
            _logger.LogDebug("ApplyFederatedRead: чат {ChatId} неизвестен → RETRY", chatId);
            throw new ChatUnknownException();
        }

        // P2-02: читатель обязан быть remote-участником этого чата, и origin события — его домашняя нода.
        var ownServer = _configuration["Federation:ServerName"] ?? string.Empty;
        var homeServer = FederationImportValidator.ResolveHomeServer(chat, readerUuid, ownServer);
        if (homeServer is null || !string.Equals(homeServer, r.OriginServer, StringComparison.OrdinalIgnoreCase))
        {
            _metrics.Increment("events_rejected.author_not_origin");
            _logger.LogWarning(
                "ApplyFederatedRead: origin {Origin} не домашняя нода читателя {ReaderUuid} чата {ChatId}",
                r.OriginServer, readerUuid, chatId);
            throw new FederatedOriginMismatchException();
        }

        var applied = await _federatedReadStatesStorage.UpsertAsync(chatId, readerUuid, upToFederatedId, originTs);
        if (!applied)
        {
            _metrics.Increment("federation_apply_read_stale");
            return new ApplyFederatedReadResponse { Applied = false };
        }

        // Локальная рассылка (Updates) — только если сообщение "до которого" уже есть в этой копии;
        // иначе прочтение всё равно сохранено и учтётся в выдаче после catch-up (2.6).
        var localMessage = upToFederatedId.HasValue
            ? await _messagesStorage.GetByFederatedIdAsync(chatId, upToFederatedId.Value)
            : null;

        if (localMessage is not null)
        {
            var localMemberIds = chat.Members?.LocalUserIds() ?? new List<long>();
            await _readByQueueSender.SendEvent(
                chatId,
                localMessage.Id,
                readBy: new List<long>(),
                newReaders: new List<long>(),
                chatMembers: localMemberIds,
                isFederated: true,
                readerUuid: readerUuid,
                upToFederatedMessageId: upToFederatedId,
                remoteParticipants: new List<FederatedParticipant>(),
                lastChangeAt: originTs);
        }

        _metrics.Increment("federation_apply_read_applied");
        _logger.LogInformation(
            "ApplyFederatedRead: применено прочтение чата {ChatId} читателем {ReaderUuid} до {UpTo}",
            chatId, readerUuid, upToFederatedId);

        return new ApplyFederatedReadResponse { Applied = true };
    }
}
