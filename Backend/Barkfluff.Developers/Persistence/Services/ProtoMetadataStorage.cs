using Barkfluff.Developers.Domain;
using Barkfluff.Developers.Infrastructure;
using Barkfluff.Developers.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Barkfluff.Developers.Persistence.Services;

public class ProtoMetadataStorage
{
    private readonly DevelopersContext _context;

    public ProtoMetadataStorage(DevelopersContext context)
    {
        _context = context;
    }

    public async Task<List<ProtoMetadata>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ProtoMetadata
            .AsNoTracking()
            .OrderBy(p => p.Order)
            .ThenBy(p => p.FileName)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProtoMetadata?> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default)
    {
        return await _context.ProtoMetadata
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.FileName == fileName, cancellationToken);
    }

    public async Task<int> SeedMissingAsync(CancellationToken cancellationToken = default)
    {
        var metadata = SeedData.GetSeedProtoMetadata();

        if (_context.Database.IsNpgsql())
        {
            var inserted = 0;
            foreach (var item in metadata)
            {
                inserted += await _context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "ProtoMetadata"
                        ("id", "file_name", "display_name", "slug", "order", "rpc_descriptions", "created_at", "updated_at")
                    VALUES
                        ({item.Id}, {item.FileName}, {item.DisplayName}, {item.Slug}, {item.Order},
                         {item.RpcDescriptions}, {item.CreatedAt}, {item.UpdatedAt})
                    ON CONFLICT ("file_name") DO NOTHING
                    """, cancellationToken);
            }

            return inserted;
        }

        var existingFileNames = await _context.ProtoMetadata
            .AsNoTracking()
            .Select(item => item.FileName)
            .ToHashSetAsync(cancellationToken);
        var missingMetadata = metadata
            .Where(item => !existingFileNames.Contains(item.FileName))
            .ToList();

        if (missingMetadata.Count == 0)
            return 0;

        _context.ProtoMetadata.AddRange(missingMetadata);
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
