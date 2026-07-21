using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.Federation;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.Persistence.Services;

// Прочтения remote-участников fed-DM (этап 2.4, docs/rearch/05-chat-replication.md, «Read receipts»).
public class FederatedReadStatesStorage
{
    private readonly MessagesContext _context;

    public FederatedReadStatesStorage(MessagesContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Upsert «прочитано до» для remote-читателя. Идемпотентно и монотонно: более старое событие
    /// (readAt меньше уже сохранённого) не откатывает более новое.
    /// </summary>
    public async Task<bool> UpsertAsync(Guid chatId, Guid userUuid, Guid? lastReadFederatedMessageId, DateTime readAt)
    {
        var existing = await _context.FederatedReadStates
            .FirstOrDefaultAsync(s => s.ChatId == chatId && s.UserUuid == userUuid);

        if (existing is null)
        {
            _context.FederatedReadStates.Add(new FederatedReadState
            {
                ChatId = chatId,
                UserUuid = userUuid,
                LastReadFederatedMessageId = lastReadFederatedMessageId,
                ReadAt = readAt,
            });
            await _context.SaveChangesAsync();
            return true;
        }

        if (!LwwResolver.ShouldApplyRead(existing.ReadAt, readAt))
            return false;

        existing.LastReadFederatedMessageId = lastReadFederatedMessageId;
        existing.ReadAt = readAt;
        await _context.SaveChangesAsync();
        return true;
    }

    public Task<List<FederatedReadState>> GetForChatAsync(Guid chatId)
    {
        return _context.FederatedReadStates.Where(s => s.ChatId == chatId).ToListAsync();
    }
}
