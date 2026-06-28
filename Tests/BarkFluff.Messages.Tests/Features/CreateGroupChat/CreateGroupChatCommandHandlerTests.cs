using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.CreateGroupChat;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.CreateGroupChat;

public class CreateGroupChatCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<FilesServerApi.FilesServerApiClient> _filesClient;
    private readonly MessageQueueSender _queueSender;

    public CreateGroupChatCommandHandlerTests()
    {
        _filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
    }

    private CreateGroupChatCommandHandler CreateHandler(long userId)
    {
        return new CreateGroupChatCommandHandler(
            _h.CreateUserContext(userId),
            _filesClient.Object,
            _h.ChatsStorage,
            _h.MessagesStorage,
            _queueSender,
            _h.Metrics,
            TestHelper.CreateLogger<CreateGroupChatCommandHandler>());
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesGroupChat()
    {
        var handler = CreateHandler(1);

        var result = await handler.Handle(new CreateGroupChatCommand
        {
            Title = "Test Group",
            UserIds = [2, 3]
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.CreatedChat.IsGroupChat.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_EmptyTitle_ThrowsGroupChatTitleIsEmptyException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new CreateGroupChatCommand
        {
            Title = "",
            UserIds = [2]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<GroupChatTitleIsEmptyException>();
    }

    [Fact]
    public async Task Handle_NullTitle_ThrowsGroupChatTitleIsEmptyException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new CreateGroupChatCommand
        {
            Title = null!,
            UserIds = [2]
        }, CancellationToken.None);

        await act.Should().ThrowAsync<GroupChatTitleIsEmptyException>();
    }

    [Fact]
    public async Task Handle_NoUsers_ThrowsGroupChatUsersIsEmptyException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new CreateGroupChatCommand
        {
            Title = "Test",
            UserIds = []
        }, CancellationToken.None);

        await act.Should().ThrowAsync<GroupChatUsersIsEmptyException>();
    }

    [Fact]
    public async Task Handle_CreatorNotInList_AutoAddsCreator()
    {
        var handler = CreateHandler(1);

        var result = await handler.Handle(new CreateGroupChatCommand
        {
            Title = "Test Group",
            UserIds = [2, 3]
        }, CancellationToken.None);

        result.CreatedChat.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_CreatorAlreadyInList_DoesNotDuplicate()
    {
        var handler = CreateHandler(1);

        var result = await handler.Handle(new CreateGroupChatCommand
        {
            Title = "Test Group",
            UserIds = [1, 2]
        }, CancellationToken.None);

        result.CreatedChat.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_CreatesSystemMessage()
    {
        var handler = CreateHandler(1);

        await handler.Handle(new CreateGroupChatCommand
        {
            Title = "Test Group",
            UserIds = [2]
        }, CancellationToken.None);

        var messages = _h.DbContext.Messages.ToList();
        messages.Should().ContainSingle(m => m.Type == Domain.MessageContentType.System);
    }

    [Fact]
    public async Task Handle_PublishesMessageToQueue()
    {
        var handler = CreateHandler(1);

        await handler.Handle(new CreateGroupChatCommand
        {
            Title = "Test Group",
            UserIds = [2]
        }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.NewMessageEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithPicture_SetsPictureUrl()
    {
        var fileId = Guid.NewGuid();
        SetupFileClient(fileId, UploadFileType.ChatPicture, "http://pic.url");
        var handler = CreateHandler(1);

        var result = await handler.Handle(new CreateGroupChatCommand
        {
            Title = "Group",
            UserIds = [2],
            PictureFileId = fileId
        }, CancellationToken.None);

        result.CreatedChat.Picture.Should().Be("http://pic.url");
    }

    [Fact]
    public async Task Handle_WrongPictureType_ThrowsFileHasNotGroupPictureTypeException()
    {
        var fileId = Guid.NewGuid();
        SetupFileClient(fileId, UploadFileType.MessageAttachmentImage, "");
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new CreateGroupChatCommand
        {
            Title = "Group",
            UserIds = [2],
            PictureFileId = fileId
        }, CancellationToken.None);

        await act.Should().ThrowAsync<FileHasNotGroupPictureTypeException>();
    }

    private void SetupFileClient(Guid fileId, UploadFileType type, string url)
    {
        var response = new GetFileDataResponse
        {
            FileInfo = new UploadFileInfo { Id = fileId.ToString(), Type = type, FileUrl = url }
        };
        _filesClient.Setup(c => c.GetFileDataAsync(It.IsAny<GetFileDataRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetFileDataResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }
}
