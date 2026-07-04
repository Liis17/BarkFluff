using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.GetChatInfo;

public class GetChatInfoCommandHandler : IRequestHandler<GetChatInfoCommand, GetChatInfoResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly ChatCache _chatCache;
    private readonly UserContext _userContext;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly ILogger<GetChatInfoCommandHandler> _logger;

    public GetChatInfoCommandHandler(
        ChatsStorage chatsStorage,
        ChatCache chatCache,
        UserContext userContext,
        UsersServerApi.UsersServerApiClient usersServerApiClient,
        ILogger<GetChatInfoCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _chatCache = chatCache;
        _userContext = userContext;
        _usersServerApiClient = usersServerApiClient;
        _logger = logger;
    }

    public async Task<GetChatInfoResponse> Handle(GetChatInfoCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Получение информации о чате {ChatId} для пользователя {UserId}",
            request.ChatId,
            _userContext.UserId
        );

        // Проверка доступа к чату (сервисные токены имеют полный доступ)
        if (_userContext.TokenType != BarkFluff.Shared.Identity.TokenType.Service)
        {
            var hasAccess = await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId);

            if (!hasAccess)
            {
                _logger.LogWarning(
                    "Пользователь {UserId} не имеет доступа к чату {ChatId}",
                    _userContext.UserId,
                    request.ChatId
                );
                throw new NoAccessToChatException();
            }
        }

        // Получение информации о чате
        var chatInfo = await _chatsStorage.GetChatInfo(request.ChatId, _userContext.UserId);

        if (chatInfo == null)
        {
            _logger.LogWarning("Чат {ChatId} не найден", request.ChatId);
            throw new ChatNotFoundException();
        }

        var response = new GetChatInfoResponse
        {
            LastMessageId = chatInfo.LastMessageId,
            FirstUnreadMessageId = chatInfo.FirstUnreadMessageId,
            IsGroupChat = chatInfo.IsGroupChat,
            CountUnread = chatInfo.CountUnread
        };

        // Для личных чатов загружаем title и picture из кэша или Users API
        // Для сервисных токенов пропускаем — нет контекста пользователя для определения собеседника
        if (!chatInfo.IsGroupChat && _userContext.TokenType != BarkFluff.Shared.Identity.TokenType.Service)
        {
            var cachedTitle = await _chatCache.GetChatName(request.ChatId, _userContext.UserId);
            var cachedPicture = await _chatCache.GetChatImage(request.ChatId, _userContext.UserId);

            if (cachedTitle != null && cachedPicture != null)
            {
                response.Title = cachedTitle;
                response.Picture = cachedPicture ?? string.Empty;

                _logger.LogDebug(
                    "Использованы кэшированные данные для личного чата {ChatId}",
                    request.ChatId
                );
            }
            else
            {
                // Загружаем данные собеседника
                var chat = await _chatsStorage.GetChat(request.ChatId);
                var memberId = chat!.Members![0].UserId == _userContext.UserId
                    ? chat.Members[1].UserId
                    : chat.Members[0].UserId;

                var userInfo = await _usersServerApiClient.GetByIdAsync(
                    new GetByIdRequest { UserId = memberId }
                );

                response.Title = $"{userInfo.User.FirstName} {userInfo.User.LastName}";
                response.Picture = userInfo.User.ProfilePicture ?? string.Empty;

                // Сохраняем в кэш
                await _chatCache.SetChatName(request.ChatId, _userContext.UserId, response.Title);
                await _chatCache.SetChatImage(request.ChatId, _userContext.UserId, response.Picture);

                _logger.LogDebug(
                    "Загружены и кэшированы данные для личного чата {ChatId} с пользователем {MemberId}",
                    request.ChatId,
                    memberId
                );
            }
        }
        else
        {
            // Для групповых чатов берем title и picture напрямую из базы
            response.Title = chatInfo.Title ?? string.Empty;
            response.Picture = chatInfo.Picture ?? string.Empty;
        }

        // Получение списка участников чата
        var chatWithMembers = await _chatsStorage.GetChat(request.ChatId);
        if (chatWithMembers?.Members != null)
        {
            response.MembersId.AddRange(chatWithMembers.Members.Select(m => m.UserId));
        }

        // Флаг отключённых уведомлений для этого чата (только для пользовательского контекста)
        if (_userContext.TokenType != BarkFluff.Shared.Identity.TokenType.Service)
        {
            var mutedResponse = await _usersServerApiClient.GetMutedChatIdsAsync(
                new GetMutedChatIdsRequest
                {
                    UserId = _userContext.UserId,
                    ChatIds = { request.ChatId.ToString() }
                });

            response.Muted = mutedResponse.MutedChatIds.Count > 0;
        }

        _logger.LogInformation(
            "Информация о чате {ChatId} успешно получена. Непрочитанных: {CountUnread}",
            request.ChatId,
            response.CountUnread
        );

        return response;
    }
}
