using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.CreatePrivateChat;

public class CreatePrivateChatCommandHandler : IRequestHandler<CreatePrivateChatCommand, CreatePrivateChatResponse>
{
    private const int MinSaltLength = 16;
    private const int MaxSaltLength = 64;
    private const int MinVerifierLength = 16;
    private const int MaxVerifierLength = 128;

    private readonly ChatsStorage _chatsStorage;
    private readonly PrivateChatInviteStore _inviteStore;
    private readonly EncryptedMessageQueueSender _queueSender;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly UserContext _userContext;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<CreatePrivateChatCommandHandler> _logger;

    public CreatePrivateChatCommandHandler(
        ChatsStorage chatsStorage,
        PrivateChatInviteStore inviteStore,
        EncryptedMessageQueueSender queueSender,
        UsersServerApi.UsersServerApiClient usersClient,
        UserContext userContext,
        MetricsCollector metrics,
        ILogger<CreatePrivateChatCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _inviteStore = inviteStore;
        _queueSender = queueSender;
        _usersClient = usersClient;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<CreatePrivateChatResponse> Handle(CreatePrivateChatCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Создание приватного чата от пользователя {UserId} к {PeerUserId}",
            _userContext.UserId, request.PeerUserId);

        if (request.PeerUserId == _userContext.UserId)
        {
            throw new SourceForSendMessageNotSetException();
        }

        if (request.KdfSalt.Length is < MinSaltLength or > MaxSaltLength)
        {
            throw new InvalidEncryptedPayloadException();
        }

        if (request.PassphraseVerifier.Length is < MinVerifierLength or > MaxVerifierLength)
        {
            throw new InvalidEncryptedPayloadException();
        }

        // Проверка существования peer-пользователя — gRPC бросит исключение если не найден
        await _usersClient.GetByIdAsync(new GetByIdRequest { UserId = request.PeerUserId });

        var creation = await _chatsStorage.CreatePrivateChat(
            _userContext.UserId,
            request.PeerUserId,
            request.KdfSalt,
            request.PassphraseVerifier);

        if (creation.Chat.PrivateInviteState != Domain.PrivateChatInviteState.Accepted)
        {
            creation.Chat.PrivateInviterUserId = creation.Chat.Members?.FirstOrDefault()?.UserId;
        }

        if (!creation.Created)
        {
            _logger.LogInformation(
                "Возвращён существующий приватный чат {ChatId} для пары {UserId}/{PeerUserId}",
                creation.Chat.Id, _userContext.UserId, request.PeerUserId);
            return new CreatePrivateChatResponse { Chat = creation.Chat.ToGrpc(), Created = false };
        }

        await _inviteStore.SetAsync(creation.Chat.Id, request.PeerUserId);

        var invitedAt = DateTime.UtcNow;
        await _queueSender.SendInvite(
            creation.Chat.Id,
            _userContext.UserId,
            request.PeerUserId,
            request.KdfSalt,
            request.PassphraseVerifier,
            invitedAt);

        _metrics.Increment("private_chats_created");
        _logger.LogInformation(
            "Приватный чат {ChatId} создан, invite отправлен пользователю {PeerUserId}",
            creation.Chat.Id, request.PeerUserId);

        return new CreatePrivateChatResponse { Chat = creation.Chat.ToGrpc(), Created = true };
    }
}
