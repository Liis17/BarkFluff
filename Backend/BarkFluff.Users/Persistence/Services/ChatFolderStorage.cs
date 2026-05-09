using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Services;

public class ChatFolderStorage
{
    private readonly UsersContext _usersContext;

    public ChatFolderStorage(UsersContext usersContext)
    {
        _usersContext = usersContext;
    }

    public Task<List<ChatFolder>> GetByOwnerAsync(long userId)
    {
        return _usersContext.ChatFolders
            .Where(f => f.OwnerUserId == userId)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .ToListAsync();
    }

    public Task<ChatFolder?> GetByFolderIdAsync(long userId, Guid folderId)
    {
        return _usersContext.ChatFolders
            .FirstOrDefaultAsync(f => f.OwnerUserId == userId && f.FolderId == folderId);
    }

    public async Task<ChatFolder> CreateAsync(long userId, string folderName, string? folderIcon)
    {
        var maxSortOrder = await _usersContext.ChatFolders
            .Where(f => f.OwnerUserId == userId)
            .Select(f => (int?)f.SortOrder)
            .MaxAsync() ?? -1;

        var folder = new ChatFolder
        {
            OwnerUserId = userId,
            FolderId = Guid.NewGuid(),
            FolderName = folderName,
            FolderIcon = folderIcon,
            ChatList = [],
            SortOrder = maxSortOrder + 1,
        };

        _usersContext.ChatFolders.Add(folder);
        await _usersContext.SaveChangesAsync();
        return folder;
    }

    public async Task<ChatFolder?> UpdateAsync(
        long userId,
        Guid folderId,
        string? folderName,
        bool updateIcon,
        string? folderIcon,
        bool updateChatList,
        Guid[]? chatList)
    {
        var folder = await GetByFolderIdAsync(userId, folderId);
        if (folder is null)
        {
            return null;
        }

        if (folderName is not null)
        {
            folder.FolderName = folderName;
        }

        if (updateIcon)
        {
            folder.FolderIcon = string.IsNullOrEmpty(folderIcon) ? null : folderIcon;
        }

        if (updateChatList)
        {
            folder.ChatList = chatList ?? [];
        }

        await _usersContext.SaveChangesAsync();
        return folder;
    }

    public async Task<bool> DeleteAsync(long userId, Guid folderId)
    {
        var folder = await GetByFolderIdAsync(userId, folderId);
        if (folder is null)
        {
            return false;
        }

        _usersContext.ChatFolders.Remove(folder);
        await _usersContext.SaveChangesAsync();
        return true;
    }

    public async Task<ChatFolder?> AddChatAsync(long userId, Guid folderId, Guid chatId)
    {
        var folder = await GetByFolderIdAsync(userId, folderId);
        if (folder is null)
        {
            return null;
        }

        if (!folder.ChatList.Contains(chatId))
        {
            folder.ChatList = [.. folder.ChatList, chatId];
            await _usersContext.SaveChangesAsync();
        }

        return folder;
    }

    public async Task<ChatFolder?> RemoveChatAsync(long userId, Guid folderId, Guid chatId)
    {
        var folder = await GetByFolderIdAsync(userId, folderId);
        if (folder is null)
        {
            return null;
        }

        if (folder.ChatList.Contains(chatId))
        {
            folder.ChatList = folder.ChatList.Where(id => id != chatId).ToArray();
            await _usersContext.SaveChangesAsync();
        }

        return folder;
    }

    public async Task ReorderAsync(long userId, IReadOnlyList<(Guid FolderId, int SortOrder)> orders)
    {
        if (orders.Count == 0)
        {
            return;
        }

        var ids = orders.Select(o => o.FolderId).ToHashSet();

        var folders = await _usersContext.ChatFolders
            .Where(f => f.OwnerUserId == userId && ids.Contains(f.FolderId))
            .ToListAsync();

        var orderMap = orders.ToDictionary(o => o.FolderId, o => o.SortOrder);

        foreach (var folder in folders)
        {
            if (orderMap.TryGetValue(folder.FolderId, out var sortOrder))
            {
                folder.SortOrder = sortOrder;
            }
        }

        await _usersContext.SaveChangesAsync();
    }
}
