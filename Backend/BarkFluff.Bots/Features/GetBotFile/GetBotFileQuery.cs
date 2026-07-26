using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.GetBotFile;

public class GetBotFileQuery : IRequest<GetFileResponse>
{
    public string FileId { get; set; } = string.Empty;
}
