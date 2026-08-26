using BarkFluff.Files.Domain;

namespace BarkFluff.Files.Persistence;

public sealed record UploadReservation(Guid FileId, UploadFileType Type, bool Created);

public sealed record UploadClaimResult(
    UploadClaimOutcome Outcome,
    Guid ReservedFileId,
    Guid? ResultFileId,
    Guid? LeaseToken,
    int RetryAfterSeconds);

public enum UploadClaimOutcome
{
    Claimed,
    Processing,
    Completed,
    NotFound,
}

public sealed record UploadStatusResult(
    string State,
    Guid? ResultFileId,
    int RetryAfterSeconds);
