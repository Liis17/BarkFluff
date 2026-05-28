using BarkFluff.Users.Features.Badges.AssignUserBadge;
using BarkFluff.Users.Features.Badges.Commands;
using BarkFluff.Users.Features.Badges.DeleteBadge;
using BarkFluff.Users.Features.Badges.GetUserBadges;
using BarkFluff.Users.Features.Badges.Queries;
using BarkFluff.Users.Features.Badges.RemoveUserBadge;
using BarkFluff.Users.Features.Badges.UpdateBadge;
using BarkFluff.Users.Features.Badges.UpdateUserBadgesPriority;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.Badges;

public class AssignUserBadgeCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_AssignsBadgeWithDefaultPriority()
    {
        var user = await _h.SeedUser();
        var badge = await _h.SeedBadge();
        var handler = new AssignUserBadgeCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<AssignUserBadgeCommandHandler>());

        var result = await handler.Handle(new AssignUserBadgeCommand { UserId = user.Id, BadgeId = badge.Id }, CancellationToken.None);

        result.UserBadge.Should().NotBeNull();
        result.UserBadge.Priority.Should().Be(1000);
    }

    [Fact]
    public async Task Handle_AssignsBadgeWithCustomPriority()
    {
        var user = await _h.SeedUser();
        var badge = await _h.SeedBadge();
        var handler = new AssignUserBadgeCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<AssignUserBadgeCommandHandler>());

        var result = await handler.Handle(new AssignUserBadgeCommand { UserId = user.Id, BadgeId = badge.Id, Priority = 5 }, CancellationToken.None);

        result.UserBadge.Priority.Should().Be(5);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
