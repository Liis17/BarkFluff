using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.DeleteChatFolder;

public class DeleteChatFolderCommand : IRequest<DeleteChatFolderResponse>, IChatFoldersMutation
{
    public string? FolderId { get; set; }
}
