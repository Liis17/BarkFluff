using BarkFluff.Federation.Host;
using BarkFluff.Federation.Persistence.Contexts;
using BarkFluff.Federation.Tests.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Federation;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;

using Grpc.Core;

using Moq;

namespace BarkFluff.Federation.Tests.Host;

public class GetUserProfileS2STests
{
    private static (Mock<UsersServerApi.UsersServerApiClient> UsersClient, FederationS2SApiService Service) Create()
    {
        var context = TestHelpers.CreateContext();
        var usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        var service = new FederationS2SApiService(
            TestHelpers.CreateConfiguration(),
            TestHelpers.CreateSigningKeyService(context),
            usersClient.Object,
            Mock.Of<MessagesServerApi.MessagesServerApiClient>(),
            context,
            new MetricsCollector());
        return (usersClient, service);
    }

    private static GetFederatedProfileResponse FoundProfile(string? avatarFileId = "file-123")
        => new()
        {
            Found = true,
            Uuid = Guid.NewGuid().ToString(),
            Username = "alice",
            FirstName = "Alice",
            LastName = "Smith",
            Bio = "bio",
            AvatarFileId = avatarFileId ?? string.Empty,
        };

    [Fact]
    public async Task GetUserProfile_NoUserSelector_ReturnsNotFound_UsersNotCalled()
    {
        var (usersClient, service) = Create();

        var response = await service.GetUserProfile(new GetUserProfileRequest(), TestHelpers.CreateCallContext());

        response.Found.Should().BeFalse();
        usersClient.Verify(c => c.GetFederatedProfileAsync(
            It.IsAny<GetFederatedProfileRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetUserProfile_ByUsername_MapsRequestAndResponse()
    {
        var (usersClient, service) = Create();
        GetFederatedProfileRequest? captured = null;
        usersClient
            .Setup(c => c.GetFederatedProfileAsync(It.IsAny<GetFederatedProfileRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Callback<GetFederatedProfileRequest, Metadata?, DateTime?, CancellationToken>((req, _, _, _) => captured = req)
            .Returns(TestHelpers.UnaryCall(FoundProfile()));

        var response = await service.GetUserProfile(
            new GetUserProfileRequest { Username = "alice" },
            TestHelpers.CreateCallContext());

        captured.Should().NotBeNull();
        captured!.Username.Should().Be("alice");

        response.Found.Should().BeTrue();
        response.Username.Should().Be("alice");
        response.FirstName.Should().Be("Alice");
        response.LastName.Should().Be("Smith");
        response.Bio.Should().Be("bio");
        response.Avatar.Should().NotBeNull();
        response.Avatar.FileId.Should().Be("file-123");
        response.Avatar.OriginServer.Should().Be(TestHelpers.OwnServerName);
    }

    [Fact]
    public async Task GetUserProfile_ByUuid_MapsRequest()
    {
        var (usersClient, service) = Create();
        GetFederatedProfileRequest? captured = null;
        var uuid = Guid.NewGuid().ToString();
        usersClient
            .Setup(c => c.GetFederatedProfileAsync(It.IsAny<GetFederatedProfileRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Callback<GetFederatedProfileRequest, Metadata?, DateTime?, CancellationToken>((req, _, _, _) => captured = req)
            .Returns(TestHelpers.UnaryCall(FoundProfile()));

        var response = await service.GetUserProfile(
            new GetUserProfileRequest { Uuid = uuid },
            TestHelpers.CreateCallContext());

        captured!.Uuid.Should().Be(uuid);
        response.Found.Should().BeTrue();
    }

    [Fact]
    public async Task GetUserProfile_ProfileNotFound_ReturnsNotFound()
    {
        var (usersClient, service) = Create();
        usersClient
            .Setup(c => c.GetFederatedProfileAsync(It.IsAny<GetFederatedProfileRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.UnaryCall(new GetFederatedProfileResponse { Found = false }));

        var response = await service.GetUserProfile(
            new GetUserProfileRequest { Username = "ghost" },
            TestHelpers.CreateCallContext());

        response.Found.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserProfile_NoAvatar_AvatarFieldEmpty()
    {
        var (usersClient, service) = Create();
        usersClient
            .Setup(c => c.GetFederatedProfileAsync(It.IsAny<GetFederatedProfileRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelpers.UnaryCall(FoundProfile(avatarFileId: null)));

        var response = await service.GetUserProfile(
            new GetUserProfileRequest { Username = "alice" },
            TestHelpers.CreateCallContext());

        response.Found.Should().BeTrue();
        response.Avatar.Should().BeNull("без AvatarFileId ссылка на файл не отдаётся");
    }
}
