using BarkFluff.Messages.Domain;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.Persistence.Services;

public class ChatsStorage
{
    private readonly MessagesContext _context;

    public ChatsStorage(MessagesContext context)
    {
        _context = context;
    }

    public async Task<List<Chat>> GetUserChats(long userId, int skip, int count)
    {
        //todo: если будет лагать, то разделить на два разных запроса 
        var chats = await _context
            .Chats
            .Include(x => x.Members)
            .Where(x => x.Members!.Any(m => m.UserId == userId))
            .Skip(skip)
            .Take(count)
            .Select(c => new Chat
            {
                Id = c.Id,
                Title = c.Title,
                Picture = c.Picture,
                IsGroupChat = c.IsGroupChat,
                Members = c.Members,
                CountUnread = _context.Messages.Count(x => x.ChatId == c.Id && !x.ReadBy.Contains(userId)),
                LastMessage = _context.Messages
                    .Where(m => m.ChatId == c.Id)
                    .OrderByDescending(m => m.SentAt)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return chats;
    }

    public async Task<Guid?> GetUserChatIdWithPerson(long person, long userId)
    {
        var chat = await _context.Chats
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => !x.IsGroupChat 
                                      && x.Members!.Any(m => m.UserId == person)
                                      && x.Members!.Any(m => m.UserId == userId));

        return chat?.Id;
    }

    public async Task<bool> CheckAccessToChat(Guid chatId, long userId)
    {
        var chat = await _context.Chats.Include(x=> x.Members).FirstOrDefaultAsync(x => x.Id == chatId);

        if (chat is null)
        {
            return false;
        }
        
        return chat.Members.Any(x => x.UserId == userId);
    }
    
    public async Task<int> GetTotalUserChats(long userId)
    {
        var count = await _context.Chats
            .CountAsync(x => x.Members.Any(c => c.UserId == userId));
        
        return count;
    }

    public async Task<List<ChatMember>> GetChatMembers(Guid chatId, int skip, int count)
    {
        var members = await _context
            .ChatMembers
            .Where(x => x.ChatId == chatId)
            .Skip(skip)
            .Take(count)
            .ToListAsync();

        return members;
    }

    public async Task<int> GetTotalChatMembers(Guid chatId)
    {
        return await _context.ChatMembers.CountAsync(x => x.ChatId == chatId);
    }

    public async Task<Chat> CreatePersonChat(long userId, long personId)
    {
        var chat = new Chat()
        {
            Members = new List<ChatMember>()
            {
                new() { UserId = userId, JoinedAt = DateTime.UtcNow },
                new ChatMember() { UserId = personId, JoinedAt = DateTime.UtcNow }
            },

            IsGroupChat = false
        };
        
        var result = await _context.Chats.AddAsync(chat);
        
        await _context.SaveChangesAsync();

        return result.Entity;
    }

    public async Task<Chat> CreateGroupChat(List<long> members,  string title, string? picture)
    {
        var chat = new Chat()
        {
            IsGroupChat = true,
            Title = title,
            Picture = picture,
            Members = members.Select(x => new ChatMember() { UserId = x, JoinedAt = DateTime.UtcNow })
                .ToList()
        };
        
        var result = await _context.Chats.AddAsync(chat);
        
        await _context.SaveChangesAsync();
        
        return result.Entity;
    }

    public async Task CreateGroupChatInfo(GroupChatInfo groupChatInfo)
    {
        await _context.GroupChatInfos.AddAsync(groupChatInfo);
        await _context.SaveChangesAsync();
    }
    
    public async Task<GroupChatInfo?> GetGroupChatInfo(Guid chatId)
    {
        return await _context.GroupChatInfos.FirstOrDefaultAsync(x => x.ChatId == chatId);
    }

    public async Task<Chat?> GetChat(Guid chatId)
    {
        return await _context
            .Chats
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == chatId);
    }

    public async Task RemoveChatMember(Guid chatId, long userId)
    {
        var chatMember = await _context.ChatMembers.FirstOrDefaultAsync(x => x.ChatId == chatId && x.UserId == userId);
        if (chatMember is null)
        {
            return;
        }
        
        _context.ChatMembers.Remove(chatMember);
        await _context.SaveChangesAsync();
    }
}