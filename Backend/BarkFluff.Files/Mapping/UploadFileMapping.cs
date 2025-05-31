using BarkFluff.Files.Domain;
using BarkFluff.Files.Helpers;
using BarkFluff.GrpcServer.Settings;
using BarkFluff.Proto.Files;
using Google.Protobuf.WellKnownTypes;
using UploadFileType = BarkFluff.Proto.Files.UploadFileType;

namespace BarkFluff.Files.Mapping;

public static class UploadFileMapping
{
    public static UploadFileInfo ToGrpc(this UploadFile file, RunSettings? runSettings = null)
    {
        return new UploadFileInfo
        {
            CreatedAt = Timestamp.FromDateTime(file.CreatedAt),
            Etag = file.Etag ?? string.Empty,
            FileName = file.Filename ?? string.Empty,
            Id = file.Id.ToString(),
            Type = (UploadFileType)(int)file.Type,
            UploadedAt = Timestamp.FromDateTime(file.UploadedAt ?? DateTime.MinValue), Uploader = file.Uploader,
            FileUrl = runSettings is null ? string.Empty : FileUrlHelper.GenerateDownloadUrl(runSettings.Host, runSettings.Http1Port, file.Id)
        };
    }
}