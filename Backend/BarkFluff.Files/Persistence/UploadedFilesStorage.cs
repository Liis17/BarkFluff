using BarkFluff.Files.Domain;
using BarkFluff.Shared.Exceptions.Files;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Persistence;

public class UploadedFilesStorage
{

    private readonly FilesContext _context;

    public UploadedFilesStorage(FilesContext context)
    {
        _context = context;
    }

    public async Task<UploadReservation> ReserveUploadAsync(
        long userId,
        Guid? clientOperationId,
        UploadFileType type,
        DateTime now,
        TimeSpan slotTtl,
        CancellationToken cancellationToken)
    {
        if (clientOperationId.HasValue)
        {
            var existing = await GetByClientOperationIdAsync(userId, clientOperationId.Value, cancellationToken);
            if (existing is not null)
            {
                EnsureSameType(existing, type);
                return new UploadReservation(existing.ReservedFileId, existing.Type, false);
            }
        }

        var file = new UploadFile
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            ExpiresAt = now + slotTtl,
            Type = type,
            Uploaders = [userId],
        };
        var operation = new UploadOperation
        {
            Id = Guid.NewGuid(),
            ClientOperationId = clientOperationId,
            UserId = userId,
            ReservedFileId = file.Id,
            Type = type,
            State = UploadOperationState.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _context.UploadedFiles.Add(file);
        _context.UploadOperations.Add(operation);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new UploadReservation(file.Id, type, true);
        }
        catch (DbUpdateException) when (clientOperationId.HasValue)
        {
            _context.ChangeTracker.Clear();
            var winner = await GetByClientOperationIdAsync(userId, clientOperationId.Value, cancellationToken);
            if (winner is null)
                throw;
            EnsureSameType(winner, type);
            return new UploadReservation(winner.ReservedFileId, winner.Type, false);
        }
    }

    public async Task<UploadClaimResult> ClaimUploadAsync(
        Guid reservedFileId,
        DateTime now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var operation = await GetOrCreateLegacyOperationAsync(reservedFileId, now, cancellationToken);
        if (operation is null)
            return new UploadClaimResult(UploadClaimOutcome.NotFound, reservedFileId, null, null, 0);
        if (operation.State == UploadOperationState.Completed)
            return CompletedClaim(operation);
        if (operation.State == UploadOperationState.Processing && operation.LeaseExpiresAt > now)
            return ProcessingClaim(operation, now);

        var leaseToken = Guid.NewGuid();
        var leaseExpiresAt = now + leaseDuration;
        var claimed = await _context.UploadOperations
            .Where(item => item.ReservedFileId == reservedFileId
                && (item.State == UploadOperationState.Pending
                    || item.State == UploadOperationState.Processing && item.LeaseExpiresAt <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.State, UploadOperationState.Processing)
                .SetProperty(item => item.LeaseToken, leaseToken)
                .SetProperty(item => item.LeaseExpiresAt, leaseExpiresAt)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

        if (claimed == 1)
        {
            await _context.UploadedFiles
                .Where(file => file.Id == reservedFileId && file.ExpiresAt < leaseExpiresAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(file => file.ExpiresAt, leaseExpiresAt), cancellationToken);
            return new UploadClaimResult(
                UploadClaimOutcome.Claimed,
                reservedFileId,
                null,
                leaseToken,
                0);
        }

        operation = await _context.UploadOperations
            .AsNoTracking()
            .SingleAsync(item => item.ReservedFileId == reservedFileId, cancellationToken);
        return operation.State == UploadOperationState.Completed
            ? CompletedClaim(operation)
            : ProcessingClaim(operation, now);
    }

    public async Task CompleteUploadAsync(
        Guid reservedFileId,
        Guid leaseToken,
        Guid resultFileId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var updated = await _context.UploadOperations
            .Where(item => item.ReservedFileId == reservedFileId
                && item.State == UploadOperationState.Processing
                && item.LeaseToken == leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.State, UploadOperationState.Completed)
                .SetProperty(item => item.ResultFileId, resultFileId)
                .SetProperty(item => item.LeaseToken, (Guid?)null)
                .SetProperty(item => item.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

        if (updated == 1)
            return;

        var operation = await _context.UploadOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ReservedFileId == reservedFileId, cancellationToken);
        if (operation is { State: UploadOperationState.Completed } && operation.ResultFileId == resultFileId)
            return;
        throw new InvalidOperationException("Upload lease is no longer owned by this request");
    }

    public async Task CompleteDeduplicatedUploadAsync(
        Guid reservedFileId,
        Guid leaseToken,
        Guid resultFileId,
        IReadOnlyCollection<long> uploaders,
        CancellationToken cancellationToken)
    {
        var operation = await _context.UploadOperations.SingleOrDefaultAsync(
            item => item.ReservedFileId == reservedFileId,
            cancellationToken);
        if (operation is not null)
            await _context.Entry(operation).ReloadAsync(cancellationToken);
        if (operation is { State: UploadOperationState.Completed } && operation.ResultFileId == resultFileId)
            return;
        if (operation is null
            || operation.State != UploadOperationState.Processing
            || operation.LeaseToken != leaseToken)
            throw new InvalidOperationException("Upload lease is no longer owned by this request");

        var resultFile = await _context.UploadedFiles.SingleAsync(
            file => file.Id == resultFileId,
            cancellationToken);
        foreach (var uploader in uploaders)
        {
            if (!resultFile.Uploaders.Contains(uploader))
                resultFile.Uploaders.Add(uploader);
        }

        if (reservedFileId != resultFileId)
        {
            var reservedFile = await _context.UploadedFiles.SingleOrDefaultAsync(
                file => file.Id == reservedFileId,
                cancellationToken);
            if (reservedFile is not null)
                _context.UploadedFiles.Remove(reservedFile);

            var staleHashes = await _context.FileHashes
                .Where(hash => hash.FileId == reservedFileId)
                .ToListAsync(cancellationToken);
            _context.FileHashes.RemoveRange(staleHashes);
        }

        operation.State = UploadOperationState.Completed;
        operation.ResultFileId = resultFileId;
        operation.LeaseToken = null;
        operation.LeaseExpiresAt = null;
        operation.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseUploadAsync(
        Guid reservedFileId,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await _context.UploadOperations
            .Where(item => item.ReservedFileId == reservedFileId
                && item.State == UploadOperationState.Processing
                && item.LeaseToken == leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.State, UploadOperationState.Pending)
                .SetProperty(item => item.LeaseToken, (Guid?)null)
                .SetProperty(item => item.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
    }

    public Task<UploadOperation?> GetUploadOperationAsync(
        Guid reservedFileId,
        CancellationToken cancellationToken = default)
    {
        return _context.UploadOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ReservedFileId == reservedFileId, cancellationToken);
    }

    public async Task<UploadStatusResult?> GetUploadStatusAsync(
        Guid reservedFileId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var operation = await GetOrCreateLegacyOperationAsync(reservedFileId, now, cancellationToken);
        if (operation is null)
            return null;

        if (operation.State == UploadOperationState.Processing && operation.LeaseExpiresAt <= now)
        {
            var released = await _context.UploadOperations
                .Where(item => item.ReservedFileId == reservedFileId
                    && item.State == UploadOperationState.Processing
                    && item.LeaseExpiresAt <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.State, UploadOperationState.Pending)
                    .SetProperty(item => item.LeaseToken, (Guid?)null)
                    .SetProperty(item => item.LeaseExpiresAt, (DateTime?)null)
                    .SetProperty(item => item.UpdatedAt, now), cancellationToken);
            if (released == 1)
                return new UploadStatusResult("pending", null, 0);

            operation = await _context.UploadOperations
                .AsNoTracking()
                .SingleAsync(item => item.ReservedFileId == reservedFileId, cancellationToken);
        }

        return operation.State switch
        {
            UploadOperationState.Completed => new UploadStatusResult(
                "completed", operation.ResultFileId ?? operation.ReservedFileId, 0),
            UploadOperationState.Processing => new UploadStatusResult(
                "processing", null, ProcessingClaim(operation, now).RetryAfterSeconds),
            _ => new UploadStatusResult("pending", null, 0),
        };
    }

    private Task<UploadOperation?> GetByClientOperationIdAsync(
        long userId,
        Guid clientOperationId,
        CancellationToken cancellationToken)
    {
        return _context.UploadOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.UserId == userId && item.ClientOperationId == clientOperationId,
                cancellationToken);
    }

    private async Task<UploadOperation?> GetOrCreateLegacyOperationAsync(
        Guid reservedFileId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var operation = await GetUploadOperationAsync(reservedFileId, cancellationToken);
        if (operation is not null)
            return operation;

        var file = await GetFile(reservedFileId, cancellationToken);
        if (file is null)
            return null;

        operation = new UploadOperation
        {
            Id = Guid.NewGuid(),
            UserId = file.Uploaders.FirstOrDefault(),
            ReservedFileId = file.Id,
            ResultFileId = string.IsNullOrEmpty(file.Etag) ? null : file.Id,
            Type = file.Type,
            State = string.IsNullOrEmpty(file.Etag)
                ? UploadOperationState.Pending
                : UploadOperationState.Completed,
            CreatedAt = file.CreatedAt,
            UpdatedAt = now,
        };
        _context.UploadOperations.Add(operation);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            _context.Entry(operation).State = EntityState.Detached;
            return operation;
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            return await GetUploadOperationAsync(reservedFileId, cancellationToken);
        }
    }

    private static void EnsureSameType(UploadOperation operation, UploadFileType type)
    {
        if (operation.Type != type)
            throw new UploadOperationTypeMismatchException();
    }

    private static UploadClaimResult CompletedClaim(UploadOperation operation)
    {
        return new UploadClaimResult(
            UploadClaimOutcome.Completed,
            operation.ReservedFileId,
            operation.ResultFileId ?? operation.ReservedFileId,
            null,
            0);
    }

    private static UploadClaimResult ProcessingClaim(UploadOperation operation, DateTime now)
    {
        var remaining = operation.LeaseExpiresAt.GetValueOrDefault(now) - now;
        var retryAfter = Math.Clamp((int)Math.Ceiling(remaining.TotalSeconds), 1, 5);
        return new UploadClaimResult(
            UploadClaimOutcome.Processing,
            operation.ReservedFileId,
            null,
            null,
            retryAfter);
    }

    public async Task<UploadFile> AddToStorage(UploadFile file)
    {
        _context.UploadedFiles.Add(file);

        await _context.SaveChangesAsync();

        return file;
    }

    public async Task UpdateFile(
        UploadFile file,
        CancellationToken cancellationToken = default)
    {
        _context.UploadedFiles.Update(file);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Adds a user to the uploaders list if not already present.
    /// </summary>
    public async Task AddUploaderToFile(
        Guid fileId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        var file = await _context.UploadedFiles.FirstOrDefaultAsync(x => x.Id == fileId, cancellationToken);
        if (file == null) return;

        if (!file.Uploaders.Contains(userId))
        {
            file.Uploaders.Add(userId);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<UploadFile?> GetFile(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.UploadedFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<List<UploadFile>> GetFiles(List<Guid> ids)
    {
        return await _context.UploadedFiles
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToListAsync();
    }

    /// <summary>
    /// Deletes an UploadFile record by its ID (used for cleanup during deduplication).
    /// </summary>
    public async Task DeleteFile(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _context.UploadedFiles.FirstOrDefaultAsync(x => x.Id == fileId, cancellationToken);
        if (file != null)
        {
            _context.UploadedFiles.Remove(file);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Bulk-удаление незагруженных слотов с истёкшим TTL. Используется фоновым сервисом очистки.
    /// </summary>
    public async Task<int> DeleteExpiredPendingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiredFiles = await _context.UploadedFiles
            .Where(file => file.UploadedAt == null
                && file.ExpiresAt < now
                && (!_context.UploadOperations.Any(operation => operation.ReservedFileId == file.Id)
                    || _context.UploadOperations.Any(operation => operation.ReservedFileId == file.Id
                        && operation.State == UploadOperationState.Pending)))
            .ToListAsync(cancellationToken);
        if (expiredFiles.Count == 0)
            return 0;

        var expiredIds = expiredFiles.Select(file => file.Id).ToList();
        var expiredOperations = await _context.UploadOperations
            .Where(operation => expiredIds.Contains(operation.ReservedFileId)
                && operation.State == UploadOperationState.Pending)
            .ToListAsync(cancellationToken);

        _context.UploadOperations.RemoveRange(expiredOperations);
        _context.UploadedFiles.RemoveRange(expiredFiles);
        await _context.SaveChangesAsync(cancellationToken);
        return expiredFiles.Count;
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
