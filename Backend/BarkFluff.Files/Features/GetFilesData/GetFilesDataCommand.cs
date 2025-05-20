using BarkFluff.Proto.Files;
using MediatR;

namespace BarkFluff.Files.Features.GetFilesData;

public class GetFilesDataCommand : IRequest<GetFilesDataResponse>
{
    public List<Guid> FileIds { get; set; }
}