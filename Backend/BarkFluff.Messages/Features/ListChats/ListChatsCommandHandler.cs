using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Mapping;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;

using Grpc.Core;

using MediatR;

using Microsoft.Extensions.Caching.Distributed;

using Chat = BarkFluff.Messages.Domain.Chat;

namespace BarkFluff.Messages.Features.ListChats;

public class ListChatsCommandHandler : IRequestHandler<ListChatsCommand, ListChatsResponse>
{
    private readonly UserContext _userContext;
    private readonly ChatsStorage _chatsStorage;
    private readonly ChatCache _chatCache;
    private readonly ChatDraftsStorage _chatDraftsStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersServerApiClient;
    private readonly FilesServerApi.FilesServerApiClient _filesServerApiClient;
    private readonly ILogger<ListChatsCommandHandler> _logger;

    public ListChatsCommandHandler(UserContext userContext, ChatsStorage chatsStorage, IDistributedCache cache,
        ChatCache chatCache, ChatDraftsStorage chatDraftsStorage, UsersServerApi.UsersServerApiClient usersServerApiClient, FilesServerApi.FilesServerApiClient filesServerApiClient,
        ILogger<ListChatsCommandHandler> logger)
    {
        _userContext = userContext;
        _chatsStorage = chatsStorage;
        _chatCache = chatCache;
        _chatDraftsStorage = chatDraftsStorage;
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

        var draftChatIds = await _chatDraftsStorage.GetDraftChatIdsAsync(
            _userContext.UserId,
            chats.Where(x => x.Type == Domain.ChatType.Regular).Select(x => x.Id).ToList());
        var draftChatIdSet = draftChatIds.ToHashSet();
        foreach (var chat in chats)
        {
            chat.HasDraft = draftChatIdSet.Contains(chat.Id);
        }

        // Имена/аватары личных чатов берём из Redis-кэша; недостающие добираем
        // ОДНИМ батч-запросом в Users (ListByIds) вместо GetById на каждый чат (N+1).
        var missing = new List<(Chat Chat, long MemberId)>();

        foreach (var chat in chats.Where(x => !x.IsGroupChat))
        {
            var chatName = await _chatCache.GetChatName(chat.Id, _userContext.UserId);

            if (chatName is null)
            {
                var memberId = chat.Members![0].UserId == _userContext.UserId
                    ? chat.Members[1].UserId
                    : chat.Members[0].UserId;

                // fed-DM с remote-собеседником (UserId = NULL) — Users.ListByIds не разрешит,
                // профиль remote-стороны тянут отдельно (Фаза 5). Кеш-мисс оставляем пустым.
                if (memberId is { } peerId)
                    missing.Add((chat, peerId));
            }
            else
            {
                chat.Title = chatName;
                chat.Picture = await _chatCache.GetChatImage(chat.Id, _userContext.UserId);
            }
        }

        if (missing.Count > 0)
        {
            var memberIds = missing.Select(m => m.MemberId).Distinct().ToList();

            _logger.LogDebug("Батч-загрузка {Count} пользователей для личных чатов", memberIds.Count);

            var usersResponse = await _usersServerApiClient.ListByIdsAsync(
                new ListByIdsRequest { Ids = { memberIds } });
            var usersById = usersResponse.Users.ToDictionary(u => u.Id);

            foreach (var (chat, memberId) in missing)
            {
                if (!usersById.TryGetValue(memberId, out var user))
                {
                    continue;
                }

                chat.Title = $"{user.FirstName} {user.LastName}";
                chat.Picture = user.ProfilePicture;

                await _chatCache.SetChatImage(chat.Id, _userContext.UserId, chat.Picture);
                await _chatCache.SetChatName(chat.Id, _userContext.UserId, chat.Title);
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
            // Federated-вложения рендерятся из снапшота (этап 3.1) — Files не дёргаем.
            .Where(a => a.OriginServer is null)
            .Where(a => !string.IsNullOrEmpty(a.FileId))
            .Select(a => a.FileId!)
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

        var grpcChats = chats.Select(x => x.ToGrpc(filesInfoMap)).ToList();

        // Отмечаем чаты, у которых пользователь отключил уведомления (per-chat mute).
        if (grpcChats.Count > 0)
        {
            try
            {
                var mutedResponse = await _usersServerApiClient.GetMutedChatIdsAsync(
                    new GetMutedChatIdsRequest
                    {
                        UserId = _userContext.UserId,
                        ChatIds = { grpcChats.Select(c => c.Id) }
                    },
                    cancellationToken: cancellationToken);

                if (mutedResponse.MutedChatIds.Count > 0)
                {
                    var mutedSet = mutedResponse.MutedChatIds.ToHashSet();
                    foreach (var grpcChat in grpcChats)
                    {
                        if (mutedSet.Contains(grpcChat.Id))
                        {
                            grpcChat.Muted = true;
                        }
                    }
                }
            }
            catch (RpcException ex) when (ex.StatusCode is
                StatusCode.Unavailable or
                StatusCode.DeadlineExceeded or
                StatusCode.ResourceExhausted)
            {
                _logger.LogWarning(
                    ex,
                    "Не удалось получить mute-статусы для пользователя {UserId}; список чатов возвращён с Muted=false",
                    _userContext.UserId);
            }
        }

        return new ListChatsResponse { Chats = { grpcChats }, TotalCount = totalCount };
    }
}
