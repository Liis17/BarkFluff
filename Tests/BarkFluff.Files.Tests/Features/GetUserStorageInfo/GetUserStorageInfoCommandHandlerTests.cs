using BarkFluff.Files.Features.GetUserStorageInfo;

namespace BarkFluff.Files.Tests.Features.GetUserStorageInfo;

public class GetUserStorageInfoCommandHandlerTests
{
    [Fact]
    public void Constructor_AcceptsDependencies()
    {
        var helper = new TestHelper();
        var storage = new Mock<BarkFluff.Files.Persistence.UploadedFilesStorage>(null!);
        var userContext = helper.CreateUserContext(1);
        var usersClient = new Mock<BarkFluff.Proto.Users.UsersServerApi.UsersServerApiClient>();
        var logger = TestHelper.CreateLogger<GetUserStorageInfoCommandHandler>();

        var handler = new GetUserStorageInfoCommandHandler(
            storage.Object,
            userContext,
            usersClient.Object,
            logger);

        handler.Should().NotBeNull();
    }
}
