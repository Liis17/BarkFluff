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

public class CreateBadgeCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ValidData_CreatesBadge()
    {
        var handler = new CreateBadgeCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<CreateBadgeCommandHandler>());

        var result = await handler.Handle(new CreateBadgeCommand
        {
            Name = "Gold",
            Description = "Gold badge",
            ImageUrl = "https://img.com/gold.png"
        }, CancellationToken.None);

        result.Badge.Should().NotBeNull();
        result.Badge.Name.Should().Be("Gold");
        result.Badge.Description.Should().Be("Gold badge");
        result.Badge.IsActive.Should().BeTrue();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
