using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.Badges.DeleteBadge;

public class DeleteBadgeCommand : IRequest<DeleteBadgeResponse>
{
    public int Id { get; init; }
}
