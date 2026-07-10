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

    public async Task<List<Chat>> GetDmChatsWithUser(long userId)
    {
        var chats = await _context.Chats
            .Include(x => x.Members)
            .Where(x => x.IsGroupChat == false
                        && x.Members!.Any(m => m.UserId == userId)).ToListAsync();

        return chats;
    }

    public async Task<List<Chat>> GetUserChats(long userId, int skip, int count)
    {
        // PRIVATE чаты не имеют обычных Message: их активность и unread считаются
        // только по EncryptedMessages, не раскрывая ciphertext в списке.
        var chats = await _context
            .Chats
            .AsNoTracking()
            .Include(x => x.Members)
            .Where(x => x.Members!.Any(m => m.UserId == userId))
            .Where(c => c.Type == ChatType.Private || _context.Messages.Any(m => m.ChatId == c.Id && !m.IsDeleted))
            .Select(c => new
            {
                Chat = c,
                LastActivityAt = c.Type == ChatType.Private
                    ? _context.EncryptedMessages
                        .Where(m => m.ChatId == c.Id && !m.IsDeleted)
                        .Max(m => (DateTime?)m.SentAt) ?? c.CreatedAt
                    : _context.Messages
                        .Where(m => m.ChatId == c.Id && !m.IsDeleted)
                        .Max(m => (DateTime?)m.SentAt) ?? c.CreatedAt
            })
            .OrderByDescending(x => x.LastActivityAt)
            .ThenBy(x => x.Chat.Id)
            .Skip(skip)
            .Take(count)
            .Select(c => new Chat
            {
                Id = c.Chat.Id,
                Title = c.Chat.Title,
                Picture = c.Chat.Picture,
                IsGroupChat = c.Chat.IsGroupChat,
                Members = c.Chat.Members,
                Type = c.Chat.Type,
                KdfSalt = c.Chat.KdfSalt,
                PassphraseVerifier = c.Chat.PassphraseVerifier,
                CreatedAt = c.Chat.CreatedAt,
                PrivateUserLowId = c.Chat.PrivateUserLowId,
                PrivateUserHighId = c.Chat.PrivateUserHighId,
                PrivateInviteState = c.Chat.PrivateInviteState,
                LastActivityAt = c.LastActivityAt,
                CountUnread = c.Chat.Type == ChatType.Private
                    ? _context.EncryptedMessages.Count(m =>
                        m.ChatId == c.Chat.Id && !m.IsDeleted && m.SenderId != userId &&
                        m.Id > _context.PrivateChatReadStates
                            .Where(s => s.ChatId == c.Chat.Id && s.UserId == userId)
                            .Select(s => s.LastReadMessageId)
                            .FirstOrDefault())
                    : _context.Messages.Count(m => m.ChatId == c.Chat.Id && !m.IsDeleted && !m.ReadBy.Contains(userId)),
                FirstUnreadMessageId = c.Chat.Type == ChatType.Private
                    ? _context.EncryptedMessages
                        .Where(m => m.ChatId == c.Chat.Id && !m.IsDeleted && m.SenderId != userId &&
                            m.Id > _context.PrivateChatReadStates
                                .Where(s => s.ChatId == c.Chat.Id && s.UserId == userId)
                                .Select(s => s.LastReadMessageId)
                                .FirstOrDefault())
                        .Min(m => (long?)m.Id)
                    : _context.Messages
                        .Where(m => m.ChatId == c.Chat.Id && !m.IsDeleted && !m.ReadBy.Contains(userId))
                        .Min(m => (long?)m.Id),
                LastMessage = c.Chat.Type == ChatType.Private
                    ? null
                    : _context.Messages
                        .Where(m => m.ChatId == c.Chat.Id && !m.IsDeleted)
                        .OrderByDescending(m => m.SentAt)
                        .FirstOrDefault()
            })
            .ToListAsync();

        // Pending private chats have only the initiator in ChatMembers. Add the
        // peer to the response model so clients can resolve its title/avatar.
        foreach (var chat in chats.Where(c => c.Type == ChatType.Private && !c.IsGroupChat))
        {
            var peerId = chat.PrivateUserLowId == userId ? chat.PrivateUserHighId : chat.PrivateUserLowId;
            if (peerId.HasValue && chat.Members?.All(m => m.UserId != peerId.Value) == true)
            {
                chat.Members.Add(new ChatMember { UserId = peerId.Value, JoinedAt = chat.CreatedAt });
            }
        }

        return chats;
    }

    public async Task<Guid?> GetUserChatIdWithPerson(long person, long userId)
    {
        if (person == userId)
        {
            // Самочат: ищем личный чат, где оба участника — один и тот же пользователь
            return await _context.Chats
                .Where(x => !x.IsGroupChat
                            && x.Type == ChatType.Regular
                            && x.Members!.Count == 2
                            && x.Members!.All(m => m.UserId == userId))
                .Select(c => new
                {
                    c.Id,
                    LastMessageSentAt = _context.Messages
                        .Where(m => m.ChatId == c.Id && !m.IsDeleted)
                        .Max(m => (DateTime?)m.SentAt)
                })
                .OrderByDescending(x => x.LastMessageSentAt)
                .ThenBy(x => x.Id)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync();
        }

        return await _context.Chats
            .Where(x => !x.IsGroupChat
                        && x.Type == ChatType.Regular
                        && x.Members!.Count == 2
                        && x.Members!.Any(m => m.UserId == person)
                        && x.Members!.Any(m => m.UserId == userId))
            .Select(c => new
            {
                c.Id,
                LastMessageSentAt = _context.Messages
                    .Where(m => m.ChatId == c.Id && !m.IsDeleted)
                    .Max(m => (DateTime?)m.SentAt)
            })
            .OrderByDescending(x => x.LastMessageSentAt)
            .ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> CheckAccessToChat(Guid chatId, long userId)
    {
        return await _context.ChatMembers
            .AnyAsync(m => m.ChatId == chatId && m.UserId == userId);
    }

    public async Task<List<Guid>> GetMemberChatIds(long userId, List<Guid> chatIds)
    {
        return await _context.ChatMembers
            .Where(m => m.UserId == userId && chatIds.Contains(m.ChatId))
            .Select(m => m.ChatId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<int> GetTotalUserChats(long userId)
    {
        // Пустой PRIVATE чат — полноценный объект списка: он нужен создателю
        // до принятия инвайта и для идемпотентного повторного открытия.
        var count = await _context.Chats
            .CountAsync(x => x.Members.Any(c => c.UserId == userId) &&
                (x.Type == ChatType.Private || _context.Messages.Any(m => m.ChatId == x.Id && !m.IsDeleted)));

        return count;
    }

    public async Task<List<ChatMember>> GetChatMembers(Guid chatId, int skip, int count)
    {
        var members = await _context
            .ChatMembers
            .Where(x => x.ChatId == chatId)
            .OrderBy(x => x.UserId)
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

    public async Task<PrivateChatCreationResult> CreatePrivateChat(
        long initiatorUserId,
        long peerUserId,
        byte[] kdfSalt,
        byte[] passphraseVerifier)
    {
        var lowUserId = Math.Min(initiatorUserId, peerUserId);
        var highUserId = Math.Max(initiatorUserId, peerUserId);

        var existing = await FindPrivateChatAsync(lowUserId, highUserId);
        if (existing is not null)
        {
            return new PrivateChatCreationResult(existing, false);
        }

        var chat = new Chat
        {
            IsGroupChat = false,
            Type = ChatType.Private,
            KdfSalt = kdfSalt,
            PassphraseVerifier = passphraseVerifier,
            PrivateUserLowId = lowUserId,
            PrivateUserHighId = highUserId,
            PrivateInviteState = PrivateChatInviteState.Pending,
            Members = new List<ChatMember>
            {
                new() { UserId = initiatorUserId, JoinedAt = DateTime.UtcNow }
            }
        };

        await _context.Chats.AddAsync(chat);
        try
        {
            await _context.SaveChangesAsync();
            return new PrivateChatCreationResult(chat, true);
        }
        catch (DbUpdateException)
        {
            // Уникальный индекс пары устраняет гонку двух одновременных Create.
            _context.Entry(chat).State = EntityState.Detached;
            foreach (var member in chat.Members)
            {
                _context.Entry(member).State = EntityState.Detached;
            }

            existing = await FindPrivateChatAsync(lowUserId, highUserId);
            if (existing is not null)
            {
                return new PrivateChatCreationResult(existing, false);
            }

            throw;
        }
    }

    public async Task<Chat> AcceptPrivateChat(Guid chatId, long inviteeUserId)
    {
        var chat = await _context.Chats
            .Include(x => x.Members)
            .FirstAsync(x => x.Id == chatId);

        if (chat.Members?.All(m => m.UserId != inviteeUserId) == true)
        {
            chat.Members.Add(new ChatMember { ChatId = chatId, UserId = inviteeUserId, JoinedAt = DateTime.UtcNow });
        }

        chat.PrivateInviteState = PrivateChatInviteState.Accepted;

        if (!await _context.PrivateChatReadStates.AnyAsync(s => s.ChatId == chatId && s.UserId == inviteeUserId))
        {
            await _context.PrivateChatReadStates.AddAsync(new PrivateChatReadState
            {
                ChatId = chatId,
                UserId = inviteeUserId,
            });
        }

        await _context.SaveChangesAsync();
        return chat;
    }

    public async Task<Chat> RejectPrivateChat(Guid chatId)
    {
        var chat = await _context.Chats
            .Include(x => x.Members)
            .FirstAsync(x => x.Id == chatId);

        chat.PrivateInviteState = PrivateChatInviteState.Rejected;
        await _context.SaveChangesAsync();
        return chat;
    }

    private Task<Chat?> FindPrivateChatAsync(long lowUserId, long highUserId)
    {
        return _context.Chats
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Type == ChatType.Private &&
                x.PrivateUserLowId == lowUserId && x.PrivateUserHighId == highUserId);
    }

    public async Task<ChatMember> AddChatMember(Guid chatId, long userId)
    {
        var member = new ChatMember
        {
            ChatId = chatId,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        };

        await _context.ChatMembers.AddAsync(member);
        await _context.SaveChangesAsync();
        return member;
    }

    public async Task DeleteChat(Guid chatId)
    {
        var chat = await _context.Chats.FirstOrDefaultAsync(x => x.Id == chatId);
        if (chat == null) return;

        _context.Chats.Remove(chat);
        await _context.SaveChangesAsync();
    }

    public async Task<Chat> CreateGroupChat(List<long> members, string title, string? picture)
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

    public async Task UpdateGroupChat(Guid chatId, string? title, string? picture)
    {
        var chat = await _context.Chats.FirstOrDefaultAsync(x => x.Id == chatId);
        if (chat is null)
        {
            return;
        }

        if (title is not null)
        {
            chat.Title = title;
        }

        if (picture is not null)
        {
            chat.Picture = picture;
        }

        await _context.SaveChangesAsync();
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

    public async Task<ChatInfoDto?> GetChatInfo(Guid chatId, long userId)
    {
        var chat = await _context.Chats
            .Where(x => x.Id == chatId)
            .Select(c => new ChatInfoDto
            {
                Title = c.Title,
                Picture = c.Picture,
                IsGroupChat = c.IsGroupChat,
                CountUnread = _context.Messages
                    .Count(m => m.ChatId == c.Id && !m.IsDeleted && !m.ReadBy.Contains(userId)),
                FirstUnreadMessageId = _context.Messages
                    .Where(m => m.ChatId == c.Id && !m.IsDeleted && !m.ReadBy.Contains(userId))
                    .Min(m => (long?)m.Id) ?? 0,
                LastMessageId = _context.Messages
                    .Where(m => m.ChatId == c.Id && !m.IsDeleted)
                    .Max(m => (long?)m.Id) ?? 0
            })
            .FirstOrDefaultAsync();

        return chat;
    }
}

public record PrivateChatCreationResult(Chat Chat, bool Created);

public class ChatInfoDto
{
    public long LastMessageId { get; set; }
    public long FirstUnreadMessageId { get; set; }
    public string? Title { get; set; }
    public string? Picture { get; set; }
    public bool IsGroupChat { get; set; }
    public long CountUnread { get; set; }
}
