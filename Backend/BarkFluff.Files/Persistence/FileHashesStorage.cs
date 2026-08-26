using BarkFluff.Files.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Persistence;

public class FileHashesStorage
{
    private readonly FilesContext _context;

    public FileHashesStorage(FilesContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Adds a new file hash to the storage. Идемпотентно относительно поля Hash:
    /// при гонке (две параллельные загрузки одинакового контента) уникальный индекс
    /// на Hash отклонит дубликат, а ON CONFLICT DO NOTHING проглотит конфликт без
    /// исключения — инвариант «одна строка на Hash» сохраняется. Проигравшая гонку
    /// загрузка сохраняет свой отдельный файл в S3 (дедупликация для неё пропускается) —
    /// это допустимый исход редкой гонки, а не порча данных.
    /// </summary>
    public async Task AddHash(FileHash fileHash, CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO "FileHashes" ("FileId", "Hash")
             VALUES ({fileHash.FileId}, {fileHash.Hash})
             ON CONFLICT ("Hash") DO NOTHING
             """,
            cancellationToken);
    }

    /// <summary>
    /// Gets the FileId associated with a given hash, or null if not found.
    /// </summary>
    public async Task<Guid?> GetFileIdByHash(string hash, CancellationToken cancellationToken = default)
    {
        var fileHash = await _context.FileHashes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Hash == hash, cancellationToken);

        return fileHash?.FileId;
    }

    /// <summary>
    /// Checks if a hash exists in the storage.
    /// </summary>
    public async Task<bool> HashExists(string hash)
    {
        return await _context.FileHashes
            .AsNoTracking()
            .AnyAsync(x => x.Hash == hash);
    }

    /// <summary>
    /// Gets the FileHash by FileId.
    /// </summary>
    public async Task<FileHash?> GetHashByFileId(Guid fileId)
    {
        return await _context.FileHashes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.FileId == fileId);
    }

    /// <summary>
    /// Удаляет запись хеша по FileId. Идемпотентно: возвращает количество удалённых строк.
    /// Используется при дедупликации, чтобы не оставлять висячие хеши при удалении файла.
    /// </summary>
    public Task<int> DeleteHashByFileId(Guid fileId, CancellationToken cancellationToken = default)
    {
        return _context.FileHashes
            .Where(x => x.FileId == fileId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
