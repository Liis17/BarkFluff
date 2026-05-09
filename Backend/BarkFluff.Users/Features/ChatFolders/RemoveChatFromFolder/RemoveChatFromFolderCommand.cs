using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.RemoveChatFromFolder;

public class RemoveChatFromFolderCommand : IRequest<RemoveChatFromFolderResponse>
{
    public string? FolderId { get; set; }

    public long ChatId { get; set; }
}
