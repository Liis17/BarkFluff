using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.ReorderChatFolders;

public class ReorderChatFoldersCommandHandler : IRequestHandler<ReorderChatFoldersCommand, ReorderChatFoldersResponse>
{
    private readonly UserContext _userContext;
    private readonly ChatFolderStorage _chatFolderStorage;
    private readonly ILogger<ReorderChatFoldersCommandHandler> _logger;

    public ReorderChatFoldersCommandHandler(
        UserContext userContext,
        ChatFolderStorage chatFolderStorage,
        ILogger<ReorderChatFoldersCommandHandler> logger)
    {
        _userContext = userContext;
        _chatFolderStorage = chatFolderStorage;
        _logger = logger;
    }

    public async Task<ReorderChatFoldersResponse> Handle(ReorderChatFoldersCommand request, CancellationToken cancellationToken)
    {
        var orders = new List<(Guid FolderId, int SortOrder)>();

        if (request.Orders is not null)
        {
            foreach (var item in request.Orders)
            {
                if (Guid.TryParse(item.FolderId, out var guid))
                {
                    orders.Add((guid, item.SortOrder));
                }
            }
        }

        await _chatFolderStorage.ReorderAsync(_userContext.UserId, orders);

        _logger.LogInformation("Порядок папок чатов обновлён для пользователя {UserId} ({Count} элементов)",
            _userContext.UserId, orders.Count);

        return new ReorderChatFoldersResponse();
    }
}
