using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.UploadStickerImage;

public class UploadStickerImageCommand : IRequest<UploadStickerImageResponse>
{
    public byte[] ImageData { get; init; } = [];

    public string Filename { get; init; } = string.Empty;
}
