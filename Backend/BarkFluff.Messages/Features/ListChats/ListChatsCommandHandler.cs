using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;

using MediatR;

using Microsoft.Extensions.Caching.Distributed;

using Chat = BarkFluff.Messages.Domain.Chat;

namespace BarkFluff.Messages.Features.ListChats;

public class ListChatsCommandHandler : IRequestHandler<ListChatsCommand, ListChatsResponse>
{
    private readonly UserContext _userContext;
    private readonly ChatsStorage _chatsStorage;
    private readonly ChatCache _chatCache;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly ILogger<ListChatsCommandHandler> _logger;

    public ListChatsCommandHandler(UserContext userContext, ChatsStorage chatsStorage, IDistributedCache cache,
        ChatCache chatCache, UsersServerApi.UsersServerApiClient usersServerApiClient, FilesServerApi.FilesServerApiClient filesServerApiClient,
        ILogger<ListChatsCommandHandler> logger)
    {
        _userContext = userContext;
        _chatsStorage = chatsStorage;
        _chatCache = chatCache;
        _usersServerApiClient = usersServerApiClient;
        _filesServerApiClient = filesServerApiClient;
        _logger = logger;
    }

    public async Task<ListChatsResponse> Handle(ListChatsCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Получение списка чатов для пользователя {UserId}. Skip: {Skip}, Size: {Size}",
            _userContext.UserId,
            request.Skip,
            request.Size
        );

        if (request.Size > 50)
        {
            _logger.LogDebug("Ограничение размера запроса с {RequestedSize} до 50", request.Size);
            request.Size = 50;
        }

        var chats = await _chatsStorage.GetUserChats(_userContext.UserId, request.Skip, request.Size);

        foreach (var chat in chats.Where(x => !x.IsGroupChat))
        {
            var chatName = await _chatCache.GetChatName(chat.Id, _userContext.UserId);

            if (chatName is null)
            {
                await LoadNameAndImageChat(chat);
            }
            else
            {
                var chatImage = await _chatCache.GetChatImage(chat.Id, _userContext.UserId);

                chat.Title = chatName;
                chat.Picture = chatImage;
            }
        }

        foreach (var groupChat in chats.Where(x => x.IsGroupChat))
        {
            groupChat.Members = [];
        }

        var totalCount = await _chatsStorage.GetTotalUserChats(_userContext.UserId);

        _logger.LogDebug(
            "Получено {ChatCount} чатов из {TotalCount} для пользователя {UserId}",
            chats.Count,
            totalCount,
            _userContext.UserId
        );

        var fileIds = chats
            .Where(c => c.LastMessage?.Content?.Attachments != null)
            .SelectMany(c => c.LastMessage!.Content!.Attachments!)
            .Select(a => a.FileId)
            .Distinct()
            .ToList();

        Dictionary<string, UploadFileInfo> filesInfoMap = new();
        if (fileIds.Any())
        {
            _logger.LogDebug("Получение информации о {FileCount} файлах вложений", fileIds.Count);
            var filesInfo = await _filesServerApiClient.GetFilesDataAsync(new GetFilesDataRequest { FileIds = { fileIds } });
            filesInfoMap = filesInfo.FilesInfos.ToDictionary(f => f.Id, f => f);
        }

        _logger.LogInformation(
            "Список чатов успешно получен для пользователя {UserId}. Возвращено: {ChatCount}",
            _userContext.UserId,
            chats.Count
        );

        return new ListChatsResponse { Chats = { chats.Select(x => x.ToGrpc(filesInfoMap)) }, TotalCount = totalCount };
    }

    private async Task LoadNameAndImageChat(Chat chat)
    {
        var memberId = chat.Members[0].UserId == _userContext.UserId ? chat.Members[1].UserId : chat.Members[0].UserId;

        _logger.LogDebug(
            "Загрузка имени и изображения для личного чата {ChatId} с пользователем {MemberId}",
            chat.Id,
            memberId
        );

        var userInfo = await _usersServerApiClient.GetByIdAsync(new GetByIdRequest() { UserId = memberId });

        chat.Title = $"{userInfo.User.FirstName} {userInfo.User.LastName}";
        chat.Picture = userInfo.User.ProfilePicture;

        await _chatCache.SetChatImage(chat.Id, _userContext.UserId, chat.Picture);
        await _chatCache.SetChatName(chat.Id, _userContext.UserId, chat.Title);
    }
}