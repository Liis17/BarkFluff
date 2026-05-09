using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.UpdateChatFolder;

public class UpdateChatFolderCommandHandler : IRequestHandler<UpdateChatFolderCommand, UpdateChatFolderResponse>
{
    private const int MaxNameLength = 64;

    private readonly UserContext _userContext;
    private readonly ChatFolderStorage _chatFolderStorage;
    private readonly ILogger<UpdateChatFolderCommandHandler> _logger;

    public UpdateChatFolderCommandHandler(
        UserContext userContext,
        ChatFolderStorage chatFolderStorage,
        ILogger<UpdateChatFolderCommandHandler> logger)
    {
        _userContext = userContext;
        _chatFolderStorage = chatFolderStorage;
        _logger = logger;
    }

    public async Task<UpdateChatFolderResponse> Handle(UpdateChatFolderCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.FolderId, out var folderId))
        {
            throw new ChatFolderNotFoundException();
        }

        string? name = null;
        if (request.FolderName is not null)
        {
            name = request.FolderName.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            {
                throw new ChatFolderInvalidNameException();
            }
        }

        var folder = await _chatFolderStorage.UpdateAsync(
            _userContext.UserId,
            folderId,
            name,
            request.UpdateIcon,
            request.FolderIcon,
            request.UpdateChatList,
            request.ChatList);

        if (folder is null)
        {
            throw new ChatFolderNotFoundException();
        }

        _logger.LogInformation("Папка чатов {FolderId} обновлена пользователем {UserId}", folder.FolderId, _userContext.UserId);

        return new UpdateChatFolderResponse
        {
            Folder = folder.ToGrpc(),
        };
    }
}
