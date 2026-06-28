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

public class GetUserBadgesQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ReturnsOnlyActiveBadges()
    {
        var user = await _h.SeedUser();
        var activeBadge = await _h.SeedBadge("Active", isActive: true);
        var inactiveBadge = await _h.SeedBadge("Inactive", isActive: false);
        await _h.UsersStorage.AssignBadgeToUserAsync(user.Id, activeBadge.Id, 1);
        await _h.UsersStorage.AssignBadgeToUserAsync(user.Id, inactiveBadge.Id, 2);

        var handler = new GetUserBadgesQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<GetUserBadgesQueryHandler>());

        var result = await handler.Handle(new GetUserBadgesQuery { UserId = user.Id }, CancellationToken.None);

        result.Badges.Should().ContainSingle(b => b.Badge.Id == activeBadge.Id);
    }

    [Fact]
    public async Task Handle_WithLimit_ReturnsLimitedBadges()
    {
        var user = await _h.SeedUser();
        var b1 = await _h.SeedBadge("B1");
        var b2 = await _h.SeedBadge("B2");
        var b3 = await _h.SeedBadge("B3");
        await _h.UsersStorage.AssignBadgeToUserAsync(user.Id, b1.Id, 1);
        await _h.UsersStorage.AssignBadgeToUserAsync(user.Id, b2.Id, 2);
        await _h.UsersStorage.AssignBadgeToUserAsync(user.Id, b3.Id, 3);

        var handler = new GetUserBadgesQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<GetUserBadgesQueryHandler>());

        var result = await handler.Handle(new GetUserBadgesQuery { UserId = user.Id, Limit = 2 }, CancellationToken.None);

        result.Badges.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoBadges_ReturnsEmpty()
    {
        var user = await _h.SeedUser();
        var handler = new GetUserBadgesQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<GetUserBadgesQueryHandler>());

        var result = await handler.Handle(new GetUserBadgesQuery { UserId = user.Id }, CancellationToken.None);

        result.Badges.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
