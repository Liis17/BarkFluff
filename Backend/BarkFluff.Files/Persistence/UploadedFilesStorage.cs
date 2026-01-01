using BarkFluff.Files.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Persistence;

public class UploadedFilesStorage
{

    private readonly FilesContext _context;

    public UploadedFilesStorage(FilesContext context)
    {
        _context = context;
    }

    public async Task<UploadFile> AddToStorage(UploadFile file)
    {
        _context.UploadedFiles.Add(file);

        await _context.SaveChangesAsync();

        return file;
    }

    public async Task UpdateFile(UploadFile file)
    {
        _context.UploadedFiles.Update(file);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Adds a user to the uploaders list if not already present.
    /// </summary>
    public async Task AddUploaderToFile(Guid fileId, long userId)
    {
        var file = await _context.UploadedFiles.FirstOrDefaultAsync(x => x.Id == fileId);
        if (file == null) return;

        if (!file.Uploaders.Contains(userId))
        {
            file.Uploaders.Add(userId);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<UploadFile?> GetFile(Guid id)
    {
        return await _context.UploadedFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<List<UploadFile>> GetFiles(List<Guid> ids)
    {
        return await _context.UploadedFiles
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToListAsync();
    }

    public async Task<UploadFile?> GetFileByPreviewId(Guid previewId)
    {
        return await _context.UploadedFiles.AsNoTracking().FirstOrDefaultAsync(x => x.PreviewId == previewId);
    }

    /// <summary>
    /// Gets the total storage used by a specific user in bytes.
    /// </summary>
    public async Task<long> GetUserStorageUsed(long userId)
    {
        return await _context.UploadedFiles
            .AsNoTracking()
            .Where(x => x.Uploaders.Contains(userId) && x.UploadedAt != null)
            .SumAsync(x => x.Size);
    }

    /// <summary>
    /// Gets storage usage by file type for a specific user.
    /// </summary>
    public async Task<Dictionary<UploadFileType, long>> GetUserStorageByType(long userId)
    {
        return await _context.UploadedFiles
            .AsNoTracking()
            .Where(x => x.Uploaders.Contains(userId) && x.UploadedAt != null)
            .GroupBy(x => x.Type)
            .Select(g => new { Type = g.Key, Size = g.Sum(x => x.Size) })
            .ToDictionaryAsync(x => x.Type, x => x.Size);
    }
}