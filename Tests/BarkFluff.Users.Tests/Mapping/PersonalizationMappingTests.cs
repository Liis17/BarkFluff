using BarkFluff.Users.Domain;
using BarkFluff.Users.Mapping;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Users.Tests.Mapping;

public class PersonalizationMappingTests
{
    [Fact]
    public void ToGrpc_MapsAllFields()
    {
        var p = new Domain.UserPersonalization
        {
            Id = 1,
            UserId = 100,
            ProfilePosterFileId = "file-123",
            ChatBackgroundFileIds = ["bg1", "bg2"],
        };

        var grpc = p.ToGrpc();

        grpc.ProfilePosterFileId.Should().Be("file-123");
        grpc.ChatBackgroundFileIds.Should().Equal("bg1", "bg2");
    }

    [Fact]
    public void ToGrpc_NullPoster_MapsToEmpty()
    {
        var p = new Domain.UserPersonalization
        {
            Id = 1,
            UserId = 100,
            ProfilePosterFileId = null,
            ChatBackgroundFileIds = [],
        };

        var grpc = p.ToGrpc();

        grpc.ProfilePosterFileId.Should().BeEmpty();
        grpc.ChatBackgroundFileIds.Should().BeEmpty();
    }
}
