using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.Persistence.Services;

public class ChatDraftsStorage
{
    private readonly MessagesContext _context;

    public ChatDraftsStorage(MessagesContext context)
    {
        _context = context;
    }

    public Task<ChatDraft?> GetAsync(Guid chatId, long userId)
    {
        return _context.ChatDrafts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ChatId == chatId && x.UserId == userId);
    }

    public Task<List<Guid>> GetDraftChatIdsAsync(long userId, IReadOnlyCollection<Guid> chatIds)
    {
        if (chatIds.Count == 0)
        {
            return Task.FromResult(new List<Guid>());
        }

        return _context.ChatDrafts
            .Where(x => x.UserId == userId && chatIds.Contains(x.ChatId))
            .Select(x => x.ChatId)
            .ToListAsync();
    }

    public async Task<ChatDraft> UpsertAsync(Guid chatId, long userId, string text, long? replyToMessageId)
    {
        var draft = await _context.ChatDrafts
            .SingleOrDefaultAsync(x => x.ChatId == chatId && x.UserId == userId);

        if (draft is null)
        {
            draft = new ChatDraft { ChatId = chatId, UserId = userId };
            await _context.ChatDrafts.AddAsync(draft);
        }

        draft.Text = text;
        draft.ReplyToMessageId = replyToMessageId;
        draft.UpdatedAt = DateTime.UtcNow;
        draft.Revision = Guid.NewGuid();

        await _context.SaveChangesAsync();
        return draft;
    }

    public async Task<bool> DeleteIfRevisionMatchesAsync(Guid chatId, long userId, Guid revision)
    {
        var draft = await _context.ChatDrafts
            .SingleOrDefaultAsync(x => x.ChatId == chatId && x.UserId == userId && x.Revision == revision);

        if (draft is null)
        {
            return false;
        }

        _context.ChatDrafts.Remove(draft);
        await _context.SaveChangesAsync();
        return true;
    }
}
