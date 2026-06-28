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

public class UpdateBadgeCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ExistingBadge_UpdatesFields()
    {
        var badge = await _h.SeedBadge("Old");
        var handler = new UpdateBadgeCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<UpdateBadgeCommandHandler>());

        var result = await handler.Handle(new UpdateBadgeCommand
        {
            Id = badge.Id,
            Name = "New",
            Description = "Updated",
            ImageUrl = "https://new.com/img.png",
            IsActive = false
        }, CancellationToken.None);

        result.Badge.Name.Should().Be("New");
        result.Badge.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NonExistingBadge_ThrowsArgumentException()
    {
        var handler = new UpdateBadgeCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<UpdateBadgeCommandHandler>());

        var act = () => handler.Handle(new UpdateBadgeCommand { Id = 999 }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
