using BarkFluff.Files.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Persistence;

public class TempFilesStorage
{
    private readonly FilesContext _context;
    private readonly IConfiguration _configuration;


    public TempFilesStorage(FilesContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<TempFile> CreateTempFile(Guid fileId)
    {
        var file = new TempFile()
        {
            OriginalFileId = fileId,
            ExpiresAt = DateTime.UtcNow + TimeSpan.FromMinutes(int.Parse(_configuration["TempFiles:ExpiresAt"]))
        };

        await _context.TempFiles.AddAsync(file);

        await _context.SaveChangesAsync();

        return file;
    }

    public async Task<TempFile?> GetTempFile(Guid tempFileId)
    {
        return await _context.TempFiles
            .Where(x => x.ExpiresAt > DateTime.UtcNow)
            .FirstOrDefaultAsync(x => x.Id == tempFileId);
    }
}