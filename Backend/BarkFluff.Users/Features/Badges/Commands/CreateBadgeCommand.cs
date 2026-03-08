using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Badges.Commands;

public class CreateBadgeCommand : IRequest<CreateBadgeResponse>
{
    public string Name { get; init; }

    public string Description { get; init; }

    public string ImageUrl { get; init; }
}