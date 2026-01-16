using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Messages.Features.GetPersonChatId;

public class GetPersonChatIdCommandHandler : IRequestHandler<GetPersonChatIdCommand, GetPersonChatIdResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly UserContext _userContext;
    private readonly ChatCache _chatCache;
    private readonly ILogger<GetPersonChatIdCommandHandler> _logger;

    public GetPersonChatIdCommandHandler(
        ChatsStorage chatsStorage,
        UsersServerApi.UsersServerApiClient usersServerApiClient,
        UserContext userContext,
        ChatCache chatCache,
        ILogger<GetPersonChatIdCommandHandler> logger)
    {
        _chatsStorage = chatsStorage;
        _usersServerApiClient = usersServerApiClient;
        _userContext = userContext;
        _chatCache = chatCache;
        _logger = logger;
    }

    public async Task<GetPersonChatIdResponse> Handle(GetPersonChatIdCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Получение ID чата между пользователем {UserId} и {TargetUserId}",
            _userContext.UserId,
            request.UserId
        );

        // Получаем информацию о пользователе
        var personResponse = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = request.UserId });

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
