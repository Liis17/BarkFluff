using BarkFluff.Users.Domain;
using BarkFluff.Users.Mapping;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Users.Tests.Mapping;

public class BadgeMappingTests
{
    [Fact]
    public void ToGrpc_Badge_MapsAllFields()
    {
        var badge = new Domain.Badge
        {
            Id = 5,
            Name = "Gold",
            Description = "Gold badge",
            ImageUrl = "https://img.com/gold.png",
            IsActive = true,
            CreatedDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var grpc = badge.ToGrpc();

        grpc.Id.Should().Be(5);
        grpc.Name.Should().Be("Gold");
        grpc.Description.Should().Be("Gold badge");
        grpc.ImageUrl.Should().Be("https://img.com/gold.png");
        grpc.IsActive.Should().BeTrue();
        grpc.CreatedDate.Should().Be(Timestamp.FromDateTime(badge.CreatedDate));
    }

    [Fact]
    public void ToGrpc_Badge_NullDescription_MapsToEmpty()
    {
        var badge = new Domain.Badge
        {
            Id = 1,
            Name = "Test",
            Description = null,
            ImageUrl = "url",
            CreatedDate = DateTime.UtcNow,
            IsActive = true,
        };

        var grpc = badge.ToGrpc();

        grpc.Description.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpc_UserBadge_MapsAllFields()
    {
        var userBadge = new UserBadge
        {
            Id = 100,
            UserId = 1,
            BadgeId = 5,
            Priority = 10,
            AssignedDate = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            Badge = new Domain.Badge
            {
                Id = 5,
                Name = "Gold",
                Description = "Gold",
                ImageUrl = "url",
                IsActive = true,
                CreatedDate = DateTime.UtcNow,
            },
        };

        var grpc = userBadge.ToGrpc();

        grpc.Priority.Should().Be(10);
        grpc.AssignedDate.Should().Be(Timestamp.FromDateTime(userBadge.AssignedDate));
        grpc.Badge.Id.Should().Be(5);
        grpc.Badge.Name.Should().Be("Gold");
    }

    [Fact]
    public void ToEntity_ReversesToGrpc()
    {
        var badge = new Domain.Badge
        {
            Id = 7,
            Name = "Silver",
            Description = "Silver badge",
            ImageUrl = "url",
            IsActive = true,
            CreatedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var grpc = badge.ToGrpc();
        var entity = grpc.ToEntity();

        entity.Id.Should().Be(badge.Id);
        entity.Name.Should().Be(badge.Name);
        entity.Description.Should().Be(badge.Description);
        entity.ImageUrl.Should().Be(badge.ImageUrl);
        entity.IsActive.Should().Be(badge.IsActive);
    }
}
