using BarkFluff.Proto.Files;

using MediatR;

using UploadFileType = BarkFluff.Files.Domain.UploadFileType;

namespace BarkFluff.Files.Features.GetUploadUrl;

public class GetUploadUrlCommand : IRequest<GetUploadUrlResponse>
{
    public Guid? ClientOperationId { get; set; }

    public UploadFileType Type { get; set; }
}
