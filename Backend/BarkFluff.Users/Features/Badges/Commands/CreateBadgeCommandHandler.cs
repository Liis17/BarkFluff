using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;
using MediatR;
using DomainBadge = BarkFluff.Users.Domain.Badge;

namespace BarkFluff.Users.Features.Badges.Commands;

public class CreateBadgeCommandHandler : IRequestHandler<CreateBadgeCommand, CreateBadgeResponse>
{
    private readonly UsersStorage _usersStorage;

    public CreateBadgeCommandHandler(UsersStorage usersStorage)
    {
        _usersStorage = usersStorage;
    }

    public async Task<CreateBadgeResponse> Handle(CreateBadgeCommand request, CancellationToken cancellationToken)
    {
        var badge = new DomainBadge
        {
            Name = request.Name,
            Description = request.Description,
            ImageUrl = request.ImageUrl
        };

        var createdBadge = await _usersStorage.CreateBadgeAsync(badge);

        return new CreateBadgeResponse
        {
            Badge = createdBadge.ToGrpc()
        };
    }
}