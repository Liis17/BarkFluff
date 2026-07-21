using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

using Microsoft.Extensions.Configuration;

namespace BarkFluff.Messages.Features.ImportFederatedChat;

// Применение входящего ChatCreated (docs/rearch/05, шаги в step-2.3/2.5):
// 1) invitee — локальный активный пользователь этой ноды (резолв через Users.GetUsersByUuid);
// 2) идемпотентность: чат с этим ChatId уже есть → OK;
// 3) privacy: invitee.DenyFederatedDm=true и чат новый → REJECTED:FederatedDmRejected (этап 2.5;
//    OutboxDispatcher origin-ноды превратит это в FederatedChatRejectedEvent);
// 4) upsert профиля инициатора (Users.UpsertRemoteUsers);
// 5) анти-дубль: Active fed-DM той же UUID-пары с другим ChatId → REJECTED:DuplicateFederatedDm
//    (протокол слияния — этап 2.7);
// 6) создать копию чата с парой участников (local + remote).
public class ImportFederatedChatCommandHandler : IRequestHandler<ImportFederatedChatCommand, ImportFederatedChatResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly IConfiguration _configuration;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<ImportFederatedChatCommandHandler> _logger;

    public ImportFederatedChatCommandHandler(
        ChatsStorage chatsStorage,
        UsersServerApi.UsersServerApiClient usersClient,
        IConfiguration configuration,
        MetricsCollector metrics,
        ILogger<ImportFederatedChatCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _usersClient = usersClient;
        _configuration = configuration;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ImportFederatedChatResponse> Handle(ImportFederatedChatCommand command, CancellationToken cancellationToken)
    {
        var r = command.Request;

        if (!Guid.TryParse(r.ChatId, out var chatId))
            throw new ChatIdNotValidException();
        if (!Guid.TryParse(r.InitiatorUuid, out var initiatorUuid))
            throw new ChatIdNotValidException();
        if (!Guid.TryParse(r.InviteeUuid, out var inviteeUuid))
            throw new UnknownInviteeException();

        var originTs = Federation.FederationImportValidator.ClampOriginTs(r.OriginTsMs);

        var ownServer = _configuration["Federation:ServerName"] ?? string.Empty;
        if (string.IsNullOrEmpty(ownServer))
        {
            // Federation выключена/не сконфигурирована — входящие fed-события не должны сюда попадать.
            _logger.LogWarning("ImportFederatedChat получен при незсконфигурированной Federation:ServerName");
            throw new ChatUnknownException();
        }

        // (1) invitee — локальный активный пользователь.
        var inviteeResp = await _usersClient.GetUsersByUuidAsync(
            new GetUsersByUuidRequest { Uuids = { inviteeUuid.ToString() } }, cancellationToken: cancellationToken);
        var invitee = inviteeResp.Users.FirstOrDefault();
        if (invitee is not { Found: true } || invitee.IsRemote || invitee.IsDeactivated || invitee.UserId == 0)
        {
            _logger.LogWarning("ImportFederatedChat: invitee {InviteeUuid} не локальный/деактивирован", inviteeUuid);
            throw new UnknownInviteeException();
        }

        // Идемпотентность: чат уже импортирован (повторная доставка ChatCreated не должна упасть,
        // даже если invitee включил запрет позже — privacy действует только на НОВЫЕ fed-чаты).
        var existing = await _chatsStorage.GetFederatedChatAsync(chatId);
        if (existing is not null)
            return new ImportFederatedChatResponse { Imported = false };

        // Privacy-отказ (этап 2.5): проверяется только для нового чата.
        if (invitee.DenyFederatedDm)
        {
            _logger.LogInformation(
                "ImportFederatedChat: invitee {InviteeUuid} запретил входящие fed-DM — чат {ChatId} отклонён",
                inviteeUuid, chatId);
            _metrics.Increment("federation_import_chat_rejected_privacy");
            throw new FederatedDmRejectedException();
        }

        // (2) upsert профиля инициатора. LocalUuidCollision/ServerNameMismatch → permanent отказ.
        var initiatorProfile = await _usersClient.UpsertRemoteUsersAsync(
            new UpsertRemoteUsersRequest
            {
                Records =
                {
                    new UpsertRemoteUserInfo
                    {
                        Uuid = initiatorUuid.ToString(),
                        Username = r.InitiatorUsername,
                        ServerName = r.InitiatorServerName,
                    },
                },
            },
            cancellationToken: cancellationToken);

        var upsertResult = initiatorProfile.Results.FirstOrDefault();
        if (upsertResult is null || !upsertResult.Ok)
        {
            _logger.LogWarning(
                "ImportFederatedChat: upsert инициатора {InitiatorUuid} отвергнут ({Reject})",
                initiatorUuid, upsertResult?.RejectReason);
            throw new RemoteProfileRejectedException();
        }

        // (3) анти-дубль по UUID-паре.
        var (uuidLow, uuidHigh) = Federation.FederatedUuidPair.Normalize(initiatorUuid, inviteeUuid);
        var pairExisting = await _chatsStorage.FindActiveFederatedChatByUuidPairAsync(uuidLow, uuidHigh);
        if (pairExisting is not null && pairExisting.Id != chatId)
        {
            _logger.LogWarning(
                "ImportFederatedChat: DuplicateFederatedDm incoming={Incoming} existing={Existing}",
                chatId, pairExisting.Id);
            throw new DuplicateFederatedDmException();
        }

        // (4) создать копию чата.
        await _chatsStorage.CreateFederatedChatAsync(
            chatId,
            invitee.UserId,
            inviteeUuid,
            initiatorUuid,
            r.InitiatorServerName,
            uuidLow,
            uuidHigh);

        _metrics.Increment("federation_import_chat_created");
        _logger.LogInformation(
            "ImportFederatedChat: создан fed-чат {ChatId} initiator={InitiatorUuid} invitee={InviteeUuid} origin_ts={OriginTs}",
            chatId, initiatorUuid, inviteeUuid, originTs);

        return new ImportFederatedChatResponse { Imported = true };
    }
}
