using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.AddChatToFolder;

public class AddChatToFolderCommandHandler : IRequestHandler<AddChatToFolderCommand, AddChatToFolderResponse>
{
    private readonly UserContext _userContext;
    private readonly ChatFolderStorage _chatFolderStorage;
    private readonly ILogger<AddChatToFolderCommandHandler> _logger;

    public AddChatToFolderCommandHandler(
        UserContext userContext,
        ChatFolderStorage chatFolderStorage,
        ILogger<AddChatToFolderCommandHandler> logger)
    {
        _userContext = userContext;
        _chatFolderStorage = chatFolderStorage;
        _logger = logger;
    }

    public async Task<AddChatToFolderResponse> Handle(AddChatToFolderCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.FolderId, out var folderId))
        {
            throw new ChatFolderNotFoundException();
        }

        var folder = await _chatFolderStorage.AddChatAsync(_userContext.UserId, folderId, request.ChatId);
        if (folder is null)
        {
            throw new ChatFolderNotFoundException();
        }

        _logger.LogInformation(
            "Чат {ChatId} добавлен в папку {FolderId} пользователем {UserId}",
            request.ChatId, folderId, _userContext.UserId);

        return new AddChatToFolderResponse
        {
            Folder = folder.ToGrpc(),
        };
    }
}
