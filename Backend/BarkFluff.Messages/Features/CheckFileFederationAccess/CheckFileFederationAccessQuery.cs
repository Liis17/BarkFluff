using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.CheckFileFederationAccess;

public class CheckFileFederationAccessQuery : IRequest<CheckFileFederationAccessResponse>
{
    public required string FileId { get; init; }

    public required string RequestingServer { get; init; }
}
