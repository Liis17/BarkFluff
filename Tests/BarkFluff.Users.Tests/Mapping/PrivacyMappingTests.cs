using BarkFluff.Users.Domain;
using BarkFluff.Users.Mapping;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Users.Tests.Mapping;

public class PrivacyMappingTests
{
    [Fact]
    public void ToGrpc_MapsAllFields()
    {
        var privacy = new Domain.Privacy
        {
            Id = 1,
            UserId = 100,
            ProfileVisibleOnSite = true,
            AvatarVisibility = Domain.ProfileFieldVisibility.All,
            BioVisibility = Domain.ProfileFieldVisibility.Friends,
            EmailVisibility = Domain.ProfileFieldVisibility.None,
            SearchVisible = true,
            OnlineVisibility = Domain.ProfileFieldVisibility.All,
        };

        var grpc = privacy.ToGrpc();

        grpc.ProfileVisibleOnSite.Should().BeTrue();
        grpc.AvatarVisibility.Should().Be(Proto.Users.ProfileFieldVisibility.All);
        grpc.BioVisibility.Should().Be(Proto.Users.ProfileFieldVisibility.Friends);
        grpc.EmailVisibility.Should().Be(Proto.Users.ProfileFieldVisibility.None);
        grpc.SearchVisible.Should().BeTrue();
        grpc.OnlineVisibility.Should().Be(Proto.Users.ProfileFieldVisibility.All);
    }

    [Fact]
    public void ToDomain_ReversesToGrpc()
    {
        var privacy = new Domain.Privacy
        {
            ProfileVisibleOnSite = false,
            AvatarVisibility = Domain.ProfileFieldVisibility.None,
            BioVisibility = Domain.ProfileFieldVisibility.None,
            EmailVisibility = Domain.ProfileFieldVisibility.Friends,
            SearchVisible = false,
            OnlineVisibility = Domain.ProfileFieldVisibility.Friends,
        };

        var grpc = privacy.ToGrpc();
        var domain = grpc.ToDomain();

        domain.ProfileVisibleOnSite.Should().BeFalse();
        domain.AvatarVisibility.Should().Be(Domain.ProfileFieldVisibility.None);
        domain.BioVisibility.Should().Be(Domain.ProfileFieldVisibility.None);
        domain.EmailVisibility.Should().Be(Domain.ProfileFieldVisibility.Friends);
        domain.SearchVisible.Should().BeFalse();
        domain.OnlineVisibility.Should().Be(Domain.ProfileFieldVisibility.Friends);
    }

    [Fact]
    public void ToGrpc_ToDomain_RoundtripPreservesAllValues()
    {
        var original = new Domain.Privacy
        {
            ProfileVisibleOnSite = true,
            AvatarVisibility = Domain.ProfileFieldVisibility.Friends,
            BioVisibility = Domain.ProfileFieldVisibility.All,
            EmailVisibility = Domain.ProfileFieldVisibility.None,
            SearchVisible = false,
            OnlineVisibility = Domain.ProfileFieldVisibility.Friends,
        };

        var roundtrip = original.ToGrpc().ToDomain();

        roundtrip.ProfileVisibleOnSite.Should().Be(original.ProfileVisibleOnSite);
        roundtrip.AvatarVisibility.Should().Be(original.AvatarVisibility);
        roundtrip.BioVisibility.Should().Be(original.BioVisibility);
        roundtrip.EmailVisibility.Should().Be(original.EmailVisibility);
        roundtrip.SearchVisible.Should().Be(original.SearchVisible);
        roundtrip.OnlineVisibility.Should().Be(original.OnlineVisibility);
    }
}
