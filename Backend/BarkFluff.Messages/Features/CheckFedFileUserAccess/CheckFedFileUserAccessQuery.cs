using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.CheckFedFileUserAccess;

public class CheckFedFileUserAccessQuery : IRequest<CheckFedFileUserAccessResponse>
{
    public required long UserId { get; init; }

    public required string OriginServer { get; init; }

    public required string FileId { get; init; }
}
