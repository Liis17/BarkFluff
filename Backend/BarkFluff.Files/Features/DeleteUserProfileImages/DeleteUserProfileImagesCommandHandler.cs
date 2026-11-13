using Amazon.S3.Model;

using BarkFluff.Files.Domain;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.Proto.Files;

using MediatR;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Files.Features.DeleteUserProfileImages;

public sealed class DeleteUserProfileImagesCommandHandler(
    FilesContext context,
    IS3BucketRegistry bucketRegistry)
    : IRequestHandler<DeleteUserProfileImagesCommand, DeleteUserProfileImagesResponse>
{
    private static readonly BarkFluff.Files.Domain.UploadFileType[] ProfileImageTypes =
        [BarkFluff.Files.Domain.UploadFileType.UserAvatar, BarkFluff.Files.Domain.UploadFileType.UserProfilePoster];

    public async Task<DeleteUserProfileImagesResponse> Handle(
        DeleteUserProfileImagesCommand request, CancellationToken cancellationToken)
    {
        var ownedFiles = await context.UploadedFiles
            .Where(file => file.Uploaders.Contains(request.UserId) && ProfileImageTypes.Contains(file.Type))
            .ToListAsync(cancellationToken);
        var targetIds = ownedFiles.Select(file => file.Id)
            .Concat(ownedFiles.Where(file => file.PreviewId.HasValue).Select(file => file.PreviewId!.Value))
            .Distinct()
            .ToArray();
        var files = await context.UploadedFiles
            .Where(file => targetIds.Contains(file.Id))
            .ToListAsync(cancellationToken);

        foreach (var file in files)
            file.Uploaders.Remove(request.UserId);

        var candidateIds = files.Where(file => file.Uploaders.Count == 0)
            .Select(file => file.Id)
            .ToHashSet();
        var referencedPreviews = await context.UploadedFiles
            .Where(file => file.PreviewId.HasValue && targetIds.Contains(file.PreviewId.Value))
            .ToListAsync(cancellationToken);
        var retainedPreviewIds = referencedPreviews
            .Where(file => !candidateIds.Contains(file.Id))
            .Select(file => file.PreviewId!.Value)
            .ToHashSet();
        candidateIds.ExceptWith(retainedPreviewIds);

        var deletedIds = new HashSet<Guid>();
        foreach (var file in files.Where(file => candidateIds.Contains(file.Id)))
        {
            var bucket = bucketRegistry.GetBucketName(file.Type);
            await bucketRegistry.GetClientForBucket(bucket).DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = bucket, Key = file.Id.ToString() }, cancellationToken);
            deletedIds.Add(file.Id);
            context.UploadedFiles.Remove(file);
        }

        foreach (var ownedFile in ownedFiles.Where(file => deletedIds.Contains(file.Id) && file.PreviewId.HasValue))
        {
            if (files.All(file => file.Id != ownedFile.PreviewId!.Value)
                && !retainedPreviewIds.Contains(ownedFile.PreviewId.Value))
            {
                var bucket = bucketRegistry.GetBucketName(ownedFile.Type);
                await bucketRegistry.GetClientForBucket(bucket).DeleteObjectAsync(
                    new DeleteObjectRequest { BucketName = bucket, Key = ownedFile.PreviewId.Value.ToString() }, cancellationToken);
            }
        }

        if (deletedIds.Count > 0)
        {
            var tempFiles = await context.TempFiles
                .Where(file => deletedIds.Contains(file.OriginalFileId))
                .ToListAsync(cancellationToken);
            context.TempFiles.RemoveRange(tempFiles);
            var hashes = await context.FileHashes.Where(hash => deletedIds.Contains(hash.FileId)).ToListAsync(cancellationToken);
            context.FileHashes.RemoveRange(hashes);
            var operations = await context.UploadOperations
                .Where(operation => deletedIds.Contains(operation.ReservedFileId)
                    || operation.ResultFileId.HasValue && deletedIds.Contains(operation.ResultFileId.Value))
                .ToListAsync(cancellationToken);
            context.UploadOperations.RemoveRange(operations);
        }

        await context.SaveChangesAsync(cancellationToken);
        return new DeleteUserProfileImagesResponse();
    }
}
