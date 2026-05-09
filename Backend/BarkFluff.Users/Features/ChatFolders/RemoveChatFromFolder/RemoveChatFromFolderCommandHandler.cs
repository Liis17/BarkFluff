using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.RemoveChatFromFolder;

public class RemoveChatFromFolderCommandHandler : IRequestHandler<RemoveChatFromFolderCommand, RemoveChatFromFolderResponse>
{
    private readonly UserContext _userContext;
    private readonly ChatFolderStorage _chatFolderStorage;
    private readonly ILogger<RemoveChatFromFolderCommandHandler> _logger;

    public RemoveChatFromFolderCommandHandler(
        UserContext userContext,
        ChatFolderStorage chatFolderStorage,
        ILogger<RemoveChatFromFolderCommandHandler> logger)
    {
        _userContext = userContext;
        _chatFolderStorage = chatFolderStorage;
        _logger = logger;
    }

    public async Task<RemoveChatFromFolderResponse> Handle(RemoveChatFromFolderCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.FolderId, out var folderId))
        {
            throw new ChatFolderNotFoundException();
        }

        var folder = await _chatFolderStorage.RemoveChatAsync(_userContext.UserId, folderId, request.ChatId);
        if (folder is null)
        {
            throw new ChatFolderNotFoundException();
        }

        _logger.LogInformation(
            "Чат {ChatId} удалён из папки {FolderId} пользователем {UserId}",
            request.ChatId, folderId, _userContext.UserId);

        return new RemoveChatFromFolderResponse
        {
            Folder = folder.ToGrpc(),
        };
    }
}
