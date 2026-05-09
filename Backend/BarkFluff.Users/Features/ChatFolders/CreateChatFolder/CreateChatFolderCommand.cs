using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.CreateChatFolder;

public class CreateChatFolderCommand : IRequest<CreateChatFolderResponse>
{
    public string? FolderName { get; set; }

    public string? FolderIcon { get; set; }
}
