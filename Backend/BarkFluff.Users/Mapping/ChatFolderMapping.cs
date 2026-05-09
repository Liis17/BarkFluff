using BarkFluff.Proto.Users;

namespace BarkFluff.Users.Mapping;

public static class ChatFolderMapping
{
    public static ChatFolderData ToGrpc(this Domain.ChatFolder domain)
    {
        var data = new ChatFolderData
        {
            FolderId = domain.FolderId.ToString(),
            FolderName = domain.FolderName,
            FolderIcon = domain.FolderIcon ?? string.Empty,
            SortOrder = domain.SortOrder,
        };
        data.ChatList.AddRange(domain.ChatList.Select(id => id.ToString()));
        return data;
    }
}
