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

    public async Task<List<ProtoMetadata>> GetAllAsync()
    {
        return await _context.ProtoMetadata
            .OrderBy(p => p.Order)
            .ToListAsync();
    }

    public async Task<ProtoMetadata?> GetByFileNameAsync(string fileName)
    {
        return await _context.ProtoMetadata
            .FirstOrDefaultAsync(p => p.FileName == fileName);
    }

    public async Task SeedIfNeeded()
    {
        if (await _context.ProtoMetadata.AnyAsync()) return;

        var metadata = SeedData.GetSeedProtoMetadata();
        _context.ProtoMetadata.AddRange(metadata);
        await _context.SaveChangesAsync();
    }
}
