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

    public async Task<List<DocumentationSection>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.DocumentationSections
            .AsNoTracking()
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<DocumentationSection?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _context.DocumentationSections
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
    }

    public async Task<DocumentationSection> CreateAsync(DocumentationSection section, CancellationToken cancellationToken = default)
    {
        _context.DocumentationSections.Add(section);
        await _context.SaveChangesAsync(cancellationToken);
        return section;
    }

    public async Task<DocumentationSection?> UpdateAsync(
        string key,
        string title,
        string type,
        int order,
        string content,
        CancellationToken cancellationToken = default)
    {
        var section = await _context.DocumentationSections.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (section == null) return null;

        section.Title = title;
        section.Type = type;
        section.Order = order;
        section.Content = content;
        section.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return section;
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var section = await _context.DocumentationSections.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (section == null) return false;

        _context.DocumentationSections.Remove(section);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> SeedMissingAsync(CancellationToken cancellationToken = default)
    {
        var sections = SeedData.GetSeedSections();

        if (_context.Database.IsNpgsql())
        {
            var inserted = 0;
            foreach (var section in sections)
            {
                inserted += await _context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "DocumentationSections"
                        ("id", "key", "title", "type", "order", "content", "created_at", "updated_at")
                    VALUES
                        ({section.Id}, {section.Key}, {section.Title}, {section.Type}, {section.Order},
                         {section.Content}, {section.CreatedAt}, {section.UpdatedAt})
                    ON CONFLICT ("key") DO NOTHING
                    """, cancellationToken);
            }

            return inserted;
        }

        var existingKeys = await _context.DocumentationSections
            .AsNoTracking()
            .Select(section => section.Key)
            .ToHashSetAsync(cancellationToken);
        var missingSections = sections
            .Where(section => !existingKeys.Contains(section.Key))
            .ToList();

        if (missingSections.Count == 0)
            return 0;

        _context.DocumentationSections.AddRange(missingSections);
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
