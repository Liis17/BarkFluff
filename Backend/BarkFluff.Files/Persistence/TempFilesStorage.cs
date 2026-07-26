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
            ExpiresAt = GetExpiresAt()
        };

        await _context.TempFiles.AddAsync(file);

        await _context.SaveChangesAsync();

        return file;
    }

    /// <summary>
    /// Пакетное создание временных ссылок одним SaveChangesAsync.
    /// Избавляет от N round-trip'ов к БД при запросе ссылок на множество файлов.
    /// </summary>
    public async Task<List<TempFile>> CreateTempFilesBatchAsync(IEnumerable<Guid> fileIds, CancellationToken cancellationToken = default)
    {
        var expiresAt = GetExpiresAt();

        var tempFiles = fileIds
            .Select(id => new TempFile { OriginalFileId = id, ExpiresAt = expiresAt })
            .ToList();

        if (tempFiles.Count == 0)
            return tempFiles;

        await _context.TempFiles.AddRangeAsync(tempFiles, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return tempFiles;
    }

    /// <summary>
    /// Пакетное создание capability-ссылок на federated-вложения (этап 3.3). Отдельный метод,
    /// а не флаг у существующего: снапшот метаданных нужен только этой ветке.
    /// </summary>
    public async Task<List<TempFile>> CreateFederatedTempFilesBatchAsync(
        IEnumerable<TempFile> tempFiles,
        CancellationToken cancellationToken = default)
    {
        var expiresAt = GetExpiresAt();

        var prepared = tempFiles.ToList();
        if (prepared.Count == 0)
            return prepared;

        foreach (var tempFile in prepared)
            tempFile.ExpiresAt = expiresAt;

        await _context.TempFiles.AddRangeAsync(prepared, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return prepared;
    }

    public async Task<TempFile?> GetTempFile(Guid tempFileId)
    {
        return await _context.TempFiles
            .Where(x => x.ExpiresAt > DateTime.UtcNow)
            .FirstOrDefaultAsync(x => x.Id == tempFileId);
    }

    /// <summary>
    /// Bulk-удаление просроченных временных ссылок. Используется фоновым сервисом очистки.
    /// </summary>
    public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        return _context.TempFiles
            .Where(x => x.ExpiresAt < DateTime.UtcNow)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private DateTime GetExpiresAt()
    {
        return DateTime.UtcNow + TimeSpan.FromMinutes(int.Parse(_configuration["TempFiles:ExpiresAt"]));
    }
}
