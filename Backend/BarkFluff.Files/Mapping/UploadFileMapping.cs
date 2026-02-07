using BarkFluff.Files.Domain;
using BarkFluff.Files.Helpers;
using BarkFluff.Proto.Files;
using Google.Protobuf.WellKnownTypes;
using UploadFileType = BarkFluff.Proto.Files.UploadFileType;

namespace BarkFluff.Files.Mapping;

public static class UploadFileMapping
{
    public static UploadFileInfo ToGrpc(this UploadFile file, string? publicBaseUrl = null)
    {
        var info = new UploadFileInfo
        {
            CreatedAt = Timestamp.FromDateTime(file.CreatedAt),
            Etag = file.Etag ?? string.Empty,
            FileName = file.Filename ?? string.Empty,
            Id = file.Id.ToString(),
            Type = (UploadFileType)(int)file.Type,
            UploadedAt = Timestamp.FromDateTime(file.UploadedAt ?? DateTime.MinValue),
            FileSize = file.Size,
            PreviewUrl = publicBaseUrl is null || file.PreviewId is null
                ? string.Empty
                : FileUrlHelper.GenerateDownloadUrl(publicBaseUrl, file.PreviewId!.Value),
            FileUrl = publicBaseUrl is null
                ? string.Empty
                : FileUrlHelper.GenerateDownloadUrl(publicBaseUrl, file.Id),
            PreviewFileId = file.PreviewId?.ToString() ?? string.Empty
        };

        info.Uploaders.AddRange(file.Uploaders);

        return info;
    }
}
