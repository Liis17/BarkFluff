using BarkFluff.Proto.Users;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

using DomainBadge = BarkFluff.Users.Domain.Badge;

namespace BarkFluff.Users.Features.Badges.Commands;

public class CreateBadgeCommandHandler : IRequestHandler<CreateBadgeCommand, CreateBadgeResponse>
{
    private readonly UsersStorage _usersStorage;
    private readonly ILogger<CreateBadgeCommandHandler> _logger;

    public CreateBadgeCommandHandler(UsersStorage usersStorage, ILogger<CreateBadgeCommandHandler> logger)
    {
        _usersStorage = usersStorage;
        _logger = logger;
    }

    public async Task<CreateBadgeResponse> Handle(CreateBadgeCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Создание нового бейджа '{Name}'. Описание: {Description}",
            request.Name,
            request.Description
        );

        var badge = new DomainBadge
        {
            Name = request.Name,
            Description = request.Description,
            ImageUrl = request.ImageUrl
        };

        var createdBadge = await _usersStorage.CreateBadgeAsync(badge);

        _logger.LogInformation(
            "Бейдж '{Name}' успешно создан. BadgeId: {BadgeId}",
            request.Name,
            createdBadge.Id
        );

        return new CreateBadgeResponse
        {
            Badge = createdBadge.ToGrpc()
        };
    }
}