using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.Badges.UpdateBadge;

public class UpdateBadgeCommand : IRequest<UpdateBadgeResponse>
{
    public int Id { get; init; }

    public string Name { get; init; }

    public string Description { get; init; }

    public string ImageUrl { get; init; }

    public bool IsActive { get; init; }
}
