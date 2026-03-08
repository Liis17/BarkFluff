using BarkFluff.Files.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Persistence;

public class BadgeImagesStorage
{
    private readonly FilesContext _context;

    public BadgeImagesStorage(FilesContext context)
    {
        _context = context;
    }

    public async Task<BadgeImage> AddAsync(BadgeImage badgeImage)
    {
        _context.BadgeImages.Add(badgeImage);
        await _context.SaveChangesAsync();
        return badgeImage;
    }

    public async Task UpdateAsync(BadgeImage badgeImage)
    {
        _context.BadgeImages.Update(badgeImage);
        await _context.SaveChangesAsync();
    }

    public async Task<BadgeImage?> GetByIdAsync(Guid id)
    {
        return await _context.BadgeImages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
    }
}
