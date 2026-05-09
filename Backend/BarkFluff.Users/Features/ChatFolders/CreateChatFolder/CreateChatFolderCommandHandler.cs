using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.CreateChatFolder;

public class CreateChatFolderCommandHandler : IRequestHandler<CreateChatFolderCommand, CreateChatFolderResponse>
{
    private const int MaxNameLength = 64;

    private readonly UserContext _userContext;
    private readonly ChatFolderStorage _chatFolderStorage;
    private readonly ILogger<CreateChatFolderCommandHandler> _logger;

    public CreateChatFolderCommandHandler(
        UserContext userContext,
        ChatFolderStorage chatFolderStorage,
        ILogger<CreateChatFolderCommandHandler> logger)
    {
        _userContext = userContext;
        _chatFolderStorage = chatFolderStorage;
        _logger = logger;
    }

    public async Task<CreateChatFolderResponse> Handle(CreateChatFolderCommand request, CancellationToken cancellationToken)
    {
        var name = request.FolderName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            throw new ChatFolderInvalidNameException();
        }

        var icon = request.FolderIcon?.Trim();
        if (string.IsNullOrEmpty(icon))
        {
            icon = null;
        }

        var folder = await _chatFolderStorage.CreateAsync(_userContext.UserId, name, icon);

        _logger.LogInformation("Создана папка чатов {FolderId} для пользователя {UserId}", folder.FolderId, _userContext.UserId);

        return new CreateChatFolderResponse
        {
            Folder = folder.ToGrpc(),
        };
    }
}
