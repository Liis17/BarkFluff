using BarkFluff.Proto.Files;
using MediatR;

namespace BarkFluff.Files.Features.GetTempDownloadUrl;

public class GetTempDownloadUrlCommand : IRequest<GetTempDownloadUrlResponse>
{
    public List<Guid> FileIds { get; set; }
}