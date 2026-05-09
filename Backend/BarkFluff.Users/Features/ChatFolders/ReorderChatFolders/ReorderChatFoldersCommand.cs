using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.ChatFolders.ReorderChatFolders;

public class ReorderChatFoldersCommand : IRequest<ReorderChatFoldersResponse>
{
    public IReadOnlyList<ChatFolderOrder>? Orders { get; set; }
}
