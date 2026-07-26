using BarkFluff.Proto.Files;

using MediatR;

namespace BarkFluff.Files.Features.CheckFedAvatarAccess;

public class CheckFedAvatarAccessCommand : IRequest<CheckFedAvatarAccessResponse>
{
    public required string FileId { get; init; }
}
