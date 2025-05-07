using BarkFluff.Proto.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.RemoveActiveSession;

public class RemoveActiveSessionCommand : IRequest<RemoveActiveSessionResponse>
{
    public long Id { get; set; }
}