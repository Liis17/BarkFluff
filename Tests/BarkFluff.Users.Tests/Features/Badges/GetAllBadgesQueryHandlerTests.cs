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

public class GetAllBadgesQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_WithoutInactive_ReturnsOnlyActive()
    {
        await _h.SeedBadge("Active1", isActive: true);
        await _h.SeedBadge("Inactive1", isActive: false);

        var handler = new GetAllBadgesQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<GetAllBadgesQueryHandler>());

        var result = await handler.Handle(new GetAllBadgesQuery { IncludeInactive = false }, CancellationToken.None);

        result.Badges.Should().OnlyContain(b => b.IsActive);
    }

    [Fact]
    public async Task Handle_WithInactive_ReturnsAll()
    {
        await _h.SeedBadge("Active1", isActive: true);
        await _h.SeedBadge("Inactive1", isActive: false);

        var handler = new GetAllBadgesQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<GetAllBadgesQueryHandler>());

        var result = await handler.Handle(new GetAllBadgesQuery { IncludeInactive = true }, CancellationToken.None);

        result.Badges.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoBadges_ReturnsEmpty()
    {
        var handler = new GetAllBadgesQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<GetAllBadgesQueryHandler>());

        var result = await handler.Handle(new GetAllBadgesQuery { IncludeInactive = false }, CancellationToken.None);

        result.Badges.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
