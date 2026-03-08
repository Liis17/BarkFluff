using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.UploadAvatarServer;

public class UploadAvatarServerCommand : IRequest<UploadAvatarServerResponse>
{
    public byte[] ImageData { get; init; } = [];

    public string Filename { get; init; } = string.Empty;

    public long UserId { get; init; }
}
