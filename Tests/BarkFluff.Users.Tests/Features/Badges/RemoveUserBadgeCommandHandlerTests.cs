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

public class RemoveUserBadgeCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ExistingBadge_ReturnsSuccess()
    {
        var user = await _h.SeedUser();
        var badge = await _h.SeedBadge();
        await _h.UsersStorage.AssignBadgeToUserAsync(user.Id, badge.Id, 1);
        var handler = new RemoveUserBadgeCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<RemoveUserBadgeCommandHandler>());

        var result = await handler.Handle(new RemoveUserBadgeCommand { UserId = user.Id, BadgeId = badge.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NonExistingBadge_ReturnsFalse()
    {
        var user = await _h.SeedUser();
        var handler = new RemoveUserBadgeCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<RemoveUserBadgeCommandHandler>());

        var result = await handler.Handle(new RemoveUserBadgeCommand { UserId = user.Id, BadgeId = 999 }, CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
