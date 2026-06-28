using BarkFluff.Files.Features.GetUserStorageInfoServer;

namespace BarkFluff.Files.Tests.Features.GetUserStorageInfoServer;

public class GetUserStorageInfoServerCommandHandlerTests
{
    [Fact]
    public void Constructor_AcceptsDependencies()
    {
        var storage = new Mock<BarkFluff.Files.Persistence.UploadedFilesStorage>(null!);
        var usersClient = new Mock<BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient>();
        var logger = TestHelper.CreateLogger<GetUserStorageInfoServerCommandHandler>();

        var handler = new GetUserStorageInfoServerCommandHandler(
            storage.Object,
            usersClient.Object,
            logger);

        handler.Should().NotBeNull();
    }
}
