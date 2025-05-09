using BarkFluff.Proto.Files;
using MediatR;

namespace BarkFluff.Files.Features.GetFileData;

public class GetFileDataCommand : IRequest<GetFileDataResponse>
{
    public Guid FileId { get; set; }
}