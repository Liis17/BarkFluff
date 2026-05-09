using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.UpdateChatFolder;

public class UpdateChatFolderCommand : IRequest<UpdateChatFolderResponse>
{
    public string? FolderId { get; set; }

    public string? FolderName { get; set; }

    public bool UpdateIcon { get; set; }

    public string? FolderIcon { get; set; }

    public bool UpdateChatList { get; set; }

    public Guid[]? ChatList { get; set; }
}
