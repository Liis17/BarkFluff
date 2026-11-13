using BarkFluff.Users.Domain;
using BarkFluff.Users.Mapping;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Users.Tests.Mapping;

public class UserMappingTests
{
    [Fact]
    public void ToGrpc_MapsAllFields()
    {
        var user = new User
        {
            Id = 12345,
            Username = "testuser",
            FirstName = "Test",
            LastName = "User",
            RegistrationDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            ProfilePicture = "https://pic.com/avatar.png",
            ProfilePicturePreviewUrl = "https://pic.com/avatar_small.png",
            Bio = "Hello world",
            StorageLimitGb = 10,
            IsDraft = false,
            IsBot = true,
        };

        var grpc = user.ToGrpc();

        grpc.Id.Should().Be(12345);
        grpc.Username.Should().Be("testuser");
        grpc.FirstName.Should().Be("Test");
        grpc.LastName.Should().Be("User");
        grpc.RegistrationDate.Should().Be(Timestamp.FromDateTime(user.RegistrationDate));
        grpc.ProfilePicture.Should().Be("https://pic.com/avatar.png");
        grpc.ProfilePicturePreview.Should().Be("https://pic.com/avatar_small.png");
        grpc.Bio.Should().Be("Hello world");
        grpc.StorageLimitGb.Should().Be(10);
        grpc.IsBot.Should().BeTrue();
    }

    [Fact]
    public void ToGrpc_NullProfilePicture_MapsToEmpty()
    {
        var user = new User
        {
            Id = 1,
            Username = "u",
            FirstName = "F",
            LastName = "L",
            RegistrationDate = DateTime.UtcNow,
            ProfilePicture = null,
            ProfilePicturePreviewUrl = null,
            Bio = null,
        };

        var grpc = user.ToGrpc();

        grpc.ProfilePicture.Should().BeEmpty();
        grpc.ProfilePicturePreview.Should().BeEmpty();
        grpc.Bio.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AccountStatus.Blocked, "Заблокированный пользователь")]
    [InlineData(AccountStatus.PendingDeletion, "Удалённый пользователь")]
    [InlineData(AccountStatus.Deleted, "Удалённый пользователь")]
    public void ToGrpc_RestrictedAccount_HidesProfile(AccountStatus status, string displayName)
    {
        var user = new User
        {
            Id = 10,
            Uuid = Guid.NewGuid(),
            Username = "private_name",
            FirstName = "Private",
            LastName = "Name",
            RegistrationDate = DateTime.UtcNow,
            Bio = "private bio",
            ProfilePicture = "private-avatar",
            ProfilePicturePreviewUrl = "private-preview",
            AccountStatus = status,
        };

        var grpc = user.ToGrpc();

        grpc.AccountStatus.Should().Be((BarkFluff.Proto.Users.AccountStatus)(int)status);
        grpc.FirstName.Should().Be(displayName);
        grpc.LastName.Should().BeEmpty();
        grpc.Username.Should().BeEmpty();
        grpc.Bio.Should().BeEmpty();
        grpc.ProfilePicture.Should().BeEmpty();
        grpc.ProfilePicturePreview.Should().BeEmpty();
    }
}
