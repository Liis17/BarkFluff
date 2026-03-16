using BarkFluff.Files.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Persistence;

public class StickerPacksStorage
{
    private readonly FilesContext _context;

    public StickerPacksStorage(FilesContext context)
    {
        _context = context;
    }

    public async Task<StickerPack> AddAsync(StickerPack stickerPack)
    {
        _context.StickerPacks.Add(stickerPack);
        await _context.SaveChangesAsync();
        return stickerPack;
    }

    public async Task UpdateAsync(StickerPack stickerPack)
    {
        _context.StickerPacks.Update(stickerPack);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var pack = await _context.StickerPacks.FindAsync(id);
        if (pack is not null)
        {
            _context.StickerPacks.Remove(pack);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<StickerPack?> GetByIdAsync(Guid id)
    {
        return await _context.StickerPacks
            .Include(x => x.Stickers)
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<StickerPack?> GetByIdWithoutStickersAsync(Guid id)
    {
        return await _context.StickerPacks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<List<StickerPack>> ListAsync(int offset, int limit)
    {
        return await _context.StickerPacks
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> GetTotalCountAsync()
    {
        return await _context.StickerPacks.CountAsync();
    }

    public async Task<Dictionary<Guid, int>> GetStickerCountsAsync(List<Guid> packIds)
    {
        return await _context.Stickers
            .Where(s => packIds.Contains(s.StickerPackId))
            .GroupBy(s => s.StickerPackId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }
}
