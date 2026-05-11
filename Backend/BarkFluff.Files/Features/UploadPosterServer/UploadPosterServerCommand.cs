using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.UploadPosterServer;

public class UploadPosterServerCommand : IRequest<UploadPosterServerResponse>
{
    public byte[] ImageData { get; init; } = [];

    public string Filename { get; init; } = string.Empty;

    public long UserId { get; init; }
}
