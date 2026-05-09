using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.AddChatToFolder;

public class AddChatToFolderCommand : IRequest<AddChatToFolderResponse>
{
    public string? FolderId { get; set; }

    public Guid ChatId { get; set; }
}
