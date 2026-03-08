using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.UploadBadgeImage;

public class UploadBadgeImageCommand : IRequest<UploadBadgeImageResponse>
{
    public byte[] ImageData { get; init; } = [];

    public string Filename { get; init; } = string.Empty;
}
