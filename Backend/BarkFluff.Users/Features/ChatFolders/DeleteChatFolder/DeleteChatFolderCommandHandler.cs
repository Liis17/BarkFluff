using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.DeleteChatFolder;

public class DeleteChatFolderCommandHandler : IRequestHandler<DeleteChatFolderCommand, DeleteChatFolderResponse>
{
    private readonly UserContext _userContext;
    private readonly ChatFolderStorage _chatFolderStorage;
    private readonly ILogger<DeleteChatFolderCommandHandler> _logger;

    public DeleteChatFolderCommandHandler(
        UserContext userContext,
        ChatFolderStorage chatFolderStorage,
        ILogger<DeleteChatFolderCommandHandler> logger)
    {
        _userContext = userContext;
        _chatFolderStorage = chatFolderStorage;
        _logger = logger;
    }

    public async Task<DeleteChatFolderResponse> Handle(DeleteChatFolderCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.FolderId, out var folderId))
        {
            throw new ChatFolderNotFoundException();
        }

        var deleted = await _chatFolderStorage.DeleteAsync(_userContext.UserId, folderId);
        if (!deleted)
        {
            throw new ChatFolderNotFoundException();
        }

        _logger.LogInformation("Папка чатов {FolderId} удалена пользователем {UserId}", folderId, _userContext.UserId);

        return new DeleteChatFolderResponse();
    }
}
