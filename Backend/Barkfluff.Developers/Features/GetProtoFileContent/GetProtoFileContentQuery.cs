using BarkFluff.Proto.Developers;
using MediatR;

namespace Barkfluff.Developers.Features.GetProtoFileContent;

public class GetProtoFileContentQuery : IRequest<GetProtoFileContentResponse>
{
    public string FileName { get; set; } = string.Empty;
}
