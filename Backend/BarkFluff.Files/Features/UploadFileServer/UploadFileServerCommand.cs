using BarkFluff.Proto.Files;

using MediatR;

using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Features.UploadFileServer;

public class UploadFileServerCommand : IRequest<UploadFileServerResponse>
{
    public byte[] Data { get; set; } = [];

    public string Filename { get; set; } = string.Empty;

    public UploadFileType FileType { get; set; }

    public long OwnerUserId { get; set; }
}
