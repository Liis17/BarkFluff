using BarkFluff.Files.Domain;
using BarkFluff.Files.Helpers;
using BarkFluff.Proto.Files;

using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Files.Mapping;

public static class StickerPackMapping
{
    public static StickerPackInfo ToGrpc(this StickerPack pack, int stickerCount = 0)
    {
        return new StickerPackInfo
        {
            Id = pack.Id.ToString(),
            CreatorUserId = pack.CreatorUserId,
            CoverStickerId = pack.CoverStickerId?.ToString() ?? string.Empty,
            Name = pack.Name,
            Description = pack.Description,
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(pack.CreatedAt, DateTimeKind.Utc)),
            StickerCount = stickerCount
        };
    }

    public static StickerInfo ToGrpc(this Sticker sticker, string? publicBaseUrl = null)
    {
        return new StickerInfo
        {
            Id = sticker.Id.ToString(),
            StickerPackId = sticker.StickerPackId.ToString(),
            FileId = sticker.FileId.ToString(),
            PreviewFileId = sticker.PreviewFileId?.ToString() ?? string.Empty,
            Emoji = sticker.Emoji,
            AddedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(sticker.AddedAt, DateTimeKind.Utc)),
            FileUrl = publicBaseUrl is null
                ? string.Empty
                : FileUrlHelper.GenerateDownloadUrl(publicBaseUrl, sticker.FileId),
            PreviewUrl = publicBaseUrl is null || sticker.PreviewFileId is null
                ? string.Empty
                : FileUrlHelper.GenerateDownloadUrl(publicBaseUrl, sticker.PreviewFileId.Value)
        };
    }
}
