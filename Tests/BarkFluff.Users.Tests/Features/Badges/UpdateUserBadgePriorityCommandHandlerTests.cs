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

public class UpdateUserBadgePriorityCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ExistingBadge_UpdatesPriority()
    {
        var user = await _h.SeedUser();
        var badge = await _h.SeedBadge();
        await _h.UsersStorage.AssignBadgeToUserAsync(user.Id, badge.Id, 100);
        var handler = new UpdateUserBadgePriorityCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<UpdateUserBadgePriorityCommandHandler>());

        var result = await handler.Handle(new UpdateUserBadgePriorityCommand
        {
            UserId = user.Id, BadgeId = badge.Id, NewPriority = 5
        }, CancellationToken.None);

        result.UserBadge.Priority.Should().Be(5);
    }

    [Fact]
    public async Task Handle_NonExistingBadge_ThrowsArgumentException()
    {
        var handler = new UpdateUserBadgePriorityCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<UpdateUserBadgePriorityCommandHandler>());

        var act = () => handler.Handle(new UpdateUserBadgePriorityCommand
        {
            UserId = 1, BadgeId = 999, NewPriority = 1
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
