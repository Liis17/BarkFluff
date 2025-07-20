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
    
    public async Task<UploadFile?> GetFile(Guid id)
    {
        return await _context.UploadedFiles
            .AsNoTracking()
            .FirstOrDefaultAsync( x=> x.Id == id);
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
}