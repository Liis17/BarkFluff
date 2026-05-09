using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.GetChatFolders;

public class GetChatFoldersQueryHandler : IRequestHandler<GetChatFoldersQuery, GetChatFoldersResponse>
{
    private readonly UserContext _userContext;
    private readonly ChatFolderStorage _chatFolderStorage;
    private readonly ILogger<GetChatFoldersQueryHandler> _logger;

    public GetChatFoldersQueryHandler(
        UserContext userContext,
        ChatFolderStorage chatFolderStorage,
        ILogger<GetChatFoldersQueryHandler> logger)
    {
        _userContext = userContext;
        _chatFolderStorage = chatFolderStorage;
        _logger = logger;
    }

    public async Task<GetChatFoldersResponse> Handle(GetChatFoldersQuery request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Запрос папок чатов для пользователя {UserId}", _userContext.UserId);

        var folders = await _chatFolderStorage.GetByOwnerAsync(_userContext.UserId);

        var response = new GetChatFoldersResponse();
        response.Folders.AddRange(folders.Select(f => f.ToGrpc()));
        return response;
    }
}
