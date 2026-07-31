using BarkFluff.Proto.Files;

namespace BarkFluff.Client.Core.Models;

public sealed record UserStorageInfo(
    long UsedBytes,
    long LimitBytes,
    IReadOnlyDictionary<UploadFileType, long> UsageByType);
