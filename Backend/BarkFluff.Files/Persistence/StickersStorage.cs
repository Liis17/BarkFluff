using BarkFluff.Files.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Persistence;

public class StickersStorage
{
    private readonly FilesContext _context;

    public StickersStorage(FilesContext context)
    {
        _context = context;
    }

    public async Task<Sticker> AddAsync(Sticker sticker)
    {
        _context.Stickers.Add(sticker);
        await _context.SaveChangesAsync();
        return sticker;
    }

    public async Task UpdateAsync(Sticker sticker)
    {
        _context.Stickers.Update(sticker);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var sticker = await _context.Stickers.FindAsync(id);
        if (sticker is not null)
        {
            _context.Stickers.Remove(sticker);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<Sticker?> GetByIdAsync(Guid id)
    {
        return await _context.Stickers
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<List<Sticker>> GetByIdsAsync(List<Guid> ids)
    {
        return await _context.Stickers
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToListAsync();
    }

    public async Task<List<Sticker>> GetByPackIdAsync(Guid packId)
    {
        return await _context.Stickers
            .AsNoTracking()
            .Where(x => x.StickerPackId == packId)
            .OrderBy(x => x.AddedAt)
            .ToListAsync();
    }
}
