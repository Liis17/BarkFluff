using Barkfluff.Developers.Domain;
using Barkfluff.Developers.Infrastructure;
using Barkfluff.Developers.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Barkfluff.Developers.Persistence.Services;

public class DocumentationStorage
{
    private readonly DevelopersContext _context;

    public DocumentationStorage(DevelopersContext context)
    {
        _context = context;
    }

    public async Task<List<DocumentationSection>> GetAllAsync()
    {
        return await _context.DocumentationSections
            .OrderBy(s => s.Order)
            .ToListAsync();
    }

    public async Task<DocumentationSection?> GetByKeyAsync(string key)
    {
        return await _context.DocumentationSections
            .FirstOrDefaultAsync(s => s.Key == key);
    }

    public async Task<DocumentationSection> CreateAsync(DocumentationSection section)
    {
        _context.DocumentationSections.Add(section);
        await _context.SaveChangesAsync();
        return section;
    }

    public async Task<DocumentationSection?> UpdateAsync(string key, string title, string type, int order, string content)
    {
        var section = await _context.DocumentationSections.FirstOrDefaultAsync(s => s.Key == key);
        if (section == null) return null;

        section.Title = title;
        section.Type = type;
        section.Order = order;
        section.Content = content;
        section.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return section;
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var section = await _context.DocumentationSections.FirstOrDefaultAsync(s => s.Key == key);
        if (section == null) return false;

        _context.DocumentationSections.Remove(section);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task SeedIfNeeded()
    {
        if (await _context.DocumentationSections.AnyAsync()) return;

        var sections = SeedData.GetSeedSections();
        _context.DocumentationSections.AddRange(sections);
        await _context.SaveChangesAsync();
    }
}
