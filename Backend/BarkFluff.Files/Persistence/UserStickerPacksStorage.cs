using BarkFluff.Files.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Persistence;

public class UserStickerPacksStorage
{
    private readonly FilesContext _context;

    public UserStickerPacksStorage(FilesContext context)
    {
        _context = context;
    }

    public Task<bool> ExistsAsync(long userId, Guid packId, CancellationToken cancellationToken = default)
    {
        return _context.UserStickerPacks
            .AnyAsync(x => x.UserId == userId && x.StickerPackId == packId, cancellationToken);
    }

    public Task<int> CountAsync(long userId, CancellationToken cancellationToken = default)
    {
        return _context.UserStickerPacks.CountAsync(x => x.UserId == userId, cancellationToken);
    }

    /// <summary>Добавляет набор пользователю; false — набор уже был добавлен (в том числе параллельным запросом).</summary>
    public async Task<bool> TryAddAsync(long userId, Guid packId, CancellationToken cancellationToken = default)
    {
        _context.UserStickerPacks.Add(new UserStickerPack
        {
            UserId = userId,
            StickerPackId = packId,
            AddedAt = DateTime.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            if (await ExistsAsync(userId, packId, cancellationToken))
                return false;
            throw;
        }
    }

    public Task<int> RemoveAsync(long userId, Guid packId, CancellationToken cancellationToken = default)
    {
        return _context.UserStickerPacks
            .Where(x => x.UserId == userId && x.StickerPackId == packId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Наборы пользователя со стикерами, недавно добавленные первыми.</summary>
    public async Task<List<StickerPack>> ListPacksWithStickersAsync(long userId, CancellationToken cancellationToken = default)
    {
        var entries = await _context.UserStickerPacks
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.AddedAt)
            .Include(x => x.StickerPack)
            .ThenInclude(p => p.Stickers.OrderBy(s => s.AddedAt))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return entries.Select(x => x.StickerPack).ToList();
    }
}
