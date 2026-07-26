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
            .Where(x => x.Members!.Any(m => m.UserId == userId)
                || (x.Type == ChatType.Private
                    && x.PrivateInviteState == PrivateChatInviteState.Pending
                    && (x.PrivateUserLowId == userId || x.PrivateUserHighId == userId)))
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
            if (chat.PrivateInviteState != PrivateChatInviteState.Accepted && chat.Members is { Count: 1 })
            {
                chat.PrivateInviterUserId = chat.Members[0].UserId;
            }

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
            .CountAsync(x => (x.Members.Any(c => c.UserId == userId)
                    || (x.Type == ChatType.Private
                        && x.PrivateInviteState == PrivateChatInviteState.Pending
                        && (x.PrivateUserLowId == userId || x.PrivateUserHighId == userId))) &&
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

    // ---- Федеративные DM (этап 2.3) ---------------------------------------

    /// <summary>
    /// FederatedStatus чата (для нефедеративных всегда Active — поле для них не используется).
    /// Null, если чат не найден.
    /// </summary>
    public Task<FederatedStatus?> GetFederatedStatusAsync(Guid chatId)
    {
        return _context.Chats
            .Where(c => c.Id == chatId)
            .Select(c => (FederatedStatus?)c.FederatedStatus)
            .FirstOrDefaultAsync();
    }

    public Task<Chat?> GetFederatedChatAsync(Guid chatId)
    {
        return _context.Chats
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == chatId && c.IsFederated);
    }

    /// <summary>
    /// Помечает fed-чат Rejected (этап 2.5, FederatedChatRejectedEvent). No-op, если чат не найден
    /// или уже не Active (идемпотентно — повторная доставка события не должна ничего сломать).
    /// </summary>
    public async Task<bool> MarkFederatedChatRejectedAsync(Guid chatId)
    {
        var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Id == chatId && c.IsFederated);
        if (chat is null || chat.FederatedStatus != Domain.FederatedStatus.Active)
            return false;

        chat.FederatedStatus = Domain.FederatedStatus.Rejected;
        await _context.SaveChangesAsync();
        return true;
    }

    public Task<Chat?> FindActiveFederatedChatByUuidPairAsync(Guid uuidLow, Guid uuidHigh)
    {
        return _context.Chats
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.IsFederated
                && c.FederatedStatus == Domain.FederatedStatus.Active
                && c.FederatedUuidLow == uuidLow
                && c.FederatedUuidHigh == uuidHigh);
    }

    /// <summary>
    /// Создать fed-DM с локальным участником и remote-участником.
    /// Возвращает существующий Active-чат пары, если гонка создания прошла раньше (unique index).
    /// </summary>
    public virtual async Task<Chat> CreateFederatedChatAsync(
        Guid chatId,
        long localUserId,
        Guid localUserUuid,
        Guid remoteUuid,
        string remoteServerName,
        Guid uuidLow,
        Guid uuidHigh)
    {
        var chat = new Chat
        {
            Id = chatId,
            IsGroupChat = false,
            Type = ChatType.Regular,
            IsFederated = true,
            FederatedStatus = Domain.FederatedStatus.Active,
            FederatedUuidLow = uuidLow,
            FederatedUuidHigh = uuidHigh,
            Members = new List<ChatMember>
            {
                new()
                {
                    UserId = localUserId,
                    UserUuid = localUserUuid,
                    JoinedAt = DateTime.UtcNow,
                },
                new()
                {
                    UserId = null,
                    UserUuid = remoteUuid,
                    ServerName = remoteServerName,
                    JoinedAt = DateTime.UtcNow,
                },
            },
        };

        _context.Chats.Add(chat);
        try
        {
            await _context.SaveChangesAsync();
            return chat;
        }
        catch (DbUpdateException)
        {
            // Уникальный индекс пары устраняет гонку одновременного создания.
            _context.Entry(chat).State = EntityState.Detached;
            foreach (var member in chat.Members)
                _context.Entry(member).State = EntityState.Detached;

            var existing = await FindActiveFederatedChatByUuidPairAsync(uuidLow, uuidHigh);
            if (existing is not null)
                return existing;

            throw;
        }
    }

    // ---- Presence/typing через федерацию (этап 4.1) -------------------------

    /// <summary>
    /// Членство пользователя в запрошенных чатах вместе с федеративным контекстом.
    /// Идентификатор задаётся либо <paramref name="userId"/>, либо <paramref name="userUuid"/>.
    /// </summary>
    /// <remarks>
    /// Метод зовётся на каждом typing-heartbeat, поэтому запросов ровно столько, сколько нужно:
    /// первый по стоимости равен прежнему <see cref="GetMemberChatIds"/>, второй (remote-участники)
    /// выполняется только когда среди чатов есть активные федеративные. Локальные и групповые
    /// чаты второй запрос не оплачивают, N+1 по чатам не возникает.
    /// </remarks>
    public virtual async Task<ChatMembershipContext> GetMembershipContext(
        long? userId,
        Guid? userUuid,
        List<Guid> chatIds)
    {
        var query = _context.ChatMembers
            .AsNoTracking()
            .Where(m => chatIds.Contains(m.ChatId));

        query = userUuid.HasValue
            ? query.Where(m => m.UserUuid == userUuid.Value)
            : query.Where(m => m.UserId == userId);

        var rows = await query
            .Select(m => new
            {
                m.ChatId,
                m.UserUuid,
                m.Chat.IsFederated,
                m.Chat.FederatedStatus,
            })
            .ToListAsync();

        var memberChatIds = rows.Select(r => r.ChatId).Distinct().ToList();

        // Для uuid-ветки эхо запрошенного uuid; для user_id-ветки — uuid из членства
        // (у локального участника fed-чата он заполнен с 2.3). В Users не ходим — hot-path.
        var requesterUuid = userUuid
            ?? rows.Select(r => r.UserUuid).FirstOrDefault(u => u.HasValue);

        var federatedChatIds = rows
            .Where(r => r.IsFederated && r.FederatedStatus == Domain.FederatedStatus.Active)
            .Select(r => r.ChatId)
            .Distinct()
            .ToList();

        if (federatedChatIds.Count == 0)
        {
            return new ChatMembershipContext(memberChatIds, requesterUuid, []);
        }

        var peers = await _context.ChatMembers
            .AsNoTracking()
            .Where(m => federatedChatIds.Contains(m.ChatId)
                && m.ServerName != null
                && m.UserUuid != null)
            .Select(m => new FederatedChatPeerRow(m.ChatId, m.UserUuid!.Value, m.ServerName!))
            .ToListAsync();

        return new ChatMembershipContext(memberChatIds, requesterUuid, peers);
    }

    /// <summary>
    /// Снапшот federated-вложения, если пользователь вправе его скачать (этап 3.3), иначе null.
    /// </summary>
    /// <remarks>
    /// Право даёт участие в чате, где это вложение лежит. Ищем в двух местах:
    /// обычные вложения и <b>форварднутые</b> — форварднувший пользователь легитимно видел файл,
    /// и получатель форварда должен уметь его открыть.
    ///
    /// Отклонение от плана 3.3: план предполагал, что forwarded-вложения лежат в jsonb и их
    /// придётся искать seq scan'ом. Фактически это обычная таблица <c>ForwardedMessageAttachment</c>
    /// с FK на вложение — запрос обычный, без сканирования.
    /// </remarks>
    public virtual async Task<FederatedAttachmentSnapshot?> GetFederatedAttachmentForUserAsync(
        long userId,
        string originServer,
        string fileId)
    {
        var chatIds = _context.ChatMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ChatId);

        var direct = await _context.Messages
            .AsNoTracking()
            .Where(m => chatIds.Contains(m.ChatId) && !m.IsDeleted)
            .SelectMany(m => m.Content!.Attachments!)
            .Where(a => a.FileId == fileId && a.OriginServer == originServer)
            .Select(a => new FederatedAttachmentSnapshot(a.FileName, a.FileSize, (int)a.Type))
            .FirstOrDefaultAsync();

        if (direct is not null)
        {
            return direct;
        }

        return await _context.Messages
            .AsNoTracking()
            .Where(m => chatIds.Contains(m.ChatId) && !m.IsDeleted)
            .SelectMany(m => m.Content!.Attachments!)
            .SelectMany(a => a.ForwardedAttachments!)
            .Where(f => f.FileId == fileId && f.OriginServer == originServer)
            // У форварднутой копии имени нет — снапшот имени хранится только у оригинала.
            .Select(f => new FederatedAttachmentSnapshot(null, f.FileSize, (int)f.Type))
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Разрешено ли отдать файл ноде <paramref name="requestingServer"/> (этап 3.2):
    /// file_id должен фигурировать во вложении активного федеративного чата, участником
    /// которого является эта нода.
    /// </summary>
    /// <remarks>
    /// Только НАШИ файлы (<c>OriginServer == null</c>): файл, пришедший с чужой ноды, мы не
    /// реэкспортируем — за ним следует идти на его origin. Удалённые сообщения исключены:
    /// после репликации delete (2.4) партнёр за таким файлом и не придёт.
    /// «Файла нет», «файл только в локальном чате» и «чат с другой нодой» снаружи неразличимы —
    /// иначе перебором file_id можно было бы выяснять, что у нас есть.
    /// </remarks>
    public virtual Task<bool> IsFileSharedWithServerAsync(string fileId, string requestingServer)
    {
        return _context.Chats
            .AsNoTracking()
            .AnyAsync(c => c.IsFederated
                && c.FederatedStatus == Domain.FederatedStatus.Active
                && c.Members!.Any(m => m.ServerName == requestingServer)
                && _context.Messages.Any(msg => msg.ChatId == c.Id
                    && !msg.IsDeleted
                    && msg.Content!.Attachments!.Any(a => a.FileId == fileId && a.OriginServer == null)));
    }

    /// <summary>
    /// Подмножество <paramref name="userUuids"/> — наших пользователей, у которых есть активный
    /// федеративный чат с нодой <paramref name="requestingServer"/> (этап 4.1, риск №42).
    /// Один запрос на весь батч.
    /// </summary>
    public virtual Task<List<Guid>> GetUuidsSharingFederatedChatWithServer(
        string requestingServer,
        List<Guid> userUuids)
    {
        return _context.ChatMembers
            .AsNoTracking()
            .Where(m => m.UserUuid != null
                && userUuids.Contains(m.UserUuid.Value)
                // Наш пользователь: у локального участника ServerName пуст.
                && m.ServerName == null
                && m.Chat.IsFederated
                && m.Chat.FederatedStatus == Domain.FederatedStatus.Active
                && m.Chat.Members!.Any(p => p.ServerName == requestingServer))
            .Select(m => m.UserUuid!.Value)
            .Distinct()
            .ToListAsync();
    }
}

public record PrivateChatCreationResult(Chat Chat, bool Created);

/// <summary>Членство + федеративный контекст для одного запроса CheckChatMembership (этап 4.1).</summary>
public record ChatMembershipContext(
    IReadOnlyList<Guid> MemberChatIds,
    Guid? RequesterUuid,
    IReadOnlyList<FederatedChatPeerRow> FederatedPeers);

/// <summary>Remote-участник активного федеративного чата: куда маршрутизировать typing/presence.</summary>
public record FederatedChatPeerRow(Guid ChatId, Guid UserUuid, string ServerName);

/// <summary>Снапшот метаданных federated-вложения, доступного пользователю (этап 3.3).</summary>
public record FederatedAttachmentSnapshot(string? FileName, long SizeBytes, int AttachmentType);

public class ChatInfoDto
{
    public long LastMessageId { get; set; }
    public long FirstUnreadMessageId { get; set; }
    public string? Title { get; set; }
    public string? Picture { get; set; }
    public bool IsGroupChat { get; set; }
    public long CountUnread { get; set; }
}
