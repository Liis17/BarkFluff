using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Messages.Features.GetPersonChatId;

public class GetPersonChatIdCommandHandler : IRequestHandler<GetPersonChatIdCommand, GetPersonChatIdResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly UserContext _userContext;
    private readonly ChatCache _chatCache;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<GetPersonChatIdCommandHandler> _logger;

    public GetPersonChatIdCommandHandler(
        ChatsStorage chatsStorage,
        UsersServerApi.UsersServerApiClient usersServerApiClient,
        UserContext userContext,
        ChatCache chatCache,
        MetricsCollector metrics,
        ILogger<GetPersonChatIdCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _usersServerApiClient = usersServerApiClient;
        _userContext = userContext;
        _chatCache = chatCache;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<GetPersonChatIdResponse> Handle(GetPersonChatIdCommand request, CancellationToken cancellationToken)
    {
        // fed-ветка: user_uuid → вернуть chat_id активного fed-DM, если он есть.
        // Авто-создание fed-чата в GetPersonChatId не делаем: клиент сначала шлёт SendMessage(user_uuid),
        // там чат создаётся как «первое сообщение» (IsFirstMessageInChat=true для fed-консьюмера).
        // Это совпадает с семантикой ChatCreated + NewMessage (docs/rearch/05, «Создание чата»).
        if (request.UserUuid is { } targetUuid && request.UserId is null)
        {
            var byUuid = await _usersServerApiClient.GetUsersByUuidAsync(
                new GetUsersByUuidRequest { Uuids = { targetUuid.ToString() } }, cancellationToken: cancellationToken);
            var target = byUuid.Users.FirstOrDefault();

            if (target is { Found: true, IsRemote: true } && !string.IsNullOrEmpty(target.ServerName))
            {
                var selfResp = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = _userContext.UserId });
                if (Guid.TryParse(selfResp.User.Uuid, out var selfUuid))
                {
                    var (lo, hi) = BarkFluff.Messages.Features.Federation.FederatedUuidPair.Normalize(selfUuid, targetUuid);
                    var existing = await _chatsStorage.FindActiveFederatedChatByUuidPairAsync(lo, hi);
                    if (existing is not null)
                        return new GetPersonChatIdResponse { ChatId = existing.Id.ToString() };
                }

                // fed-чата ещё нет — отдаём пустой; клиент резолвит перед отправкой и шлёт SendMessage.
                return new GetPersonChatIdResponse { ChatId = string.Empty };
            }

            // Не remote — fallback на локальный путь ниже, используя resolved UserId.
            if (target is { Found: true, IsRemote: false })
                request.UserId = target.UserId;
        }

        _logger.LogInformation(
            "Получение ID чата между пользователем {UserId} и {TargetUserId}",
            _userContext.UserId,
            request.UserId
        );

        // Получаем информацию о пользователе
        var personResponse = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = request.UserId!.Value });

        // Проверяем существование чата
        var existingChatId = await _chatsStorage.GetUserChatIdWithPerson(personResponse.User.Id, _userContext.UserId);

        Guid chatId;

        if (existingChatId is null)
        {
            _logger.LogInformation(
                "Создание нового личного чата между пользователями {UserId} и {TargetUserId}",
                _userContext.UserId,
                personResponse.User.Id
            );

            // Создаём новый чат
            var createdChat = await _chatsStorage.CreatePersonChat(_userContext.UserId, personResponse.User.Id);
            chatId = createdChat.Id;

            // Получаем информацию о текущем пользователе
            var userResponse = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = _userContext.UserId });

            // Кэшируем имена и аватары для обоих участников
            await _chatCache.SetChatName(chatId, _userContext.UserId, $"{personResponse.User.FirstName} {personResponse.User.LastName}");
            await _chatCache.SetChatName(chatId, personResponse.User.Id, $"{userResponse.User.FirstName} {userResponse.User.LastName}");

            await _chatCache.SetChatImage(chatId, _userContext.UserId, personResponse.User.ProfilePicture);
            await _chatCache.SetChatImage(chatId, personResponse.User.Id, userResponse.User.ProfilePicture);

            _metrics.Increment("chats_created_person");

            _logger.LogInformation(
                "Создан новый личный чат {ChatId} между пользователями {UserId} и {TargetUserId}",
                chatId,
                _userContext.UserId,
                personResponse.User.Id
            );
        }
        else
        {
            chatId = existingChatId.Value;
            _logger.LogDebug(
                "Найден существующий чат {ChatId} между пользователями {UserId} и {TargetUserId}",
                chatId,
                _userContext.UserId,
                request.UserId
            );
        }

        return new GetPersonChatIdResponse { ChatId = chatId.ToString() };
    }
}
