using BarkFluff.Messages.Features.UpdateGroupChat;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.UpdateGroupChat;

public class UpdateGroupChatCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<FilesServerApi.FilesServerApiClient> _filesClient;
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly MessageQueueSender _queueSender;

    public UpdateGroupChatCommandHandlerTests()
    {
        _filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
        SetupUsersClient();
    }

    private UpdateGroupChatCommandHandler CreateHandler(long userId)
    {
        return new UpdateGroupChatCommandHandler(
            _h.ChatsStorage,
            _h.CreateUserContext(userId),
            _h.MessagesStorage,
            _filesClient.Object,
            _usersClient.Object,
            _queueSender,
            _h.Metrics,
            TestHelper.CreateLogger<UpdateGroupChatCommandHandler>());
    }

    private void SetupUsersClient()
    {
        _usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns<GetByIdRequest, Metadata, DateTime?, CancellationToken>((req, _, _, _) =>
                new AsyncUnaryCall<GetByIdResponse>(
                    Task.FromResult(new GetByIdResponse
                    {
                        User = new User { Id = req.UserId, FirstName = "Test", LastName = "User" }
                    }),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { }));
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

    [Fact]
    public async Task Handle_RegularMemberChangesTitle_UpdatesTitle()
    {
        var adminId = 1L;
        var memberId = 2L;
        var chat = await _h.SeedChat(isGroupChat: true, title: "Old", memberUserIds: [adminId, memberId]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(memberId);

        var result = await handler.Handle(new UpdateGroupChatCommand { ChatId = chat.Id, Title = "New" }, CancellationToken.None);

        result.Chat.Title.Should().Be("New");
        (await _h.ChatsStorage.GetChat(chat.Id))!.Title.Should().Be("New");
    }

    [Fact]
    public async Task Handle_RegularMemberChangesAvatar_UpdatesPicture()
    {
        var adminId = 1L;
        var memberId = 2L;
        var fileId = Guid.NewGuid();
        SetupFileClient(fileId, UploadFileType.ChatPicture, "http://pic.url");
        var chat = await _h.SeedChat(isGroupChat: true, title: "Group", memberUserIds: [adminId, memberId]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(memberId);

        var result = await handler.Handle(new UpdateGroupChatCommand { ChatId = chat.Id, PictureFileId = fileId }, CancellationToken.None);

        result.Chat.Picture.Should().Be("http://pic.url");
        (await _h.ChatsStorage.GetChat(chat.Id))!.Picture.Should().Be("http://pic.url");
    }

    [Fact]
    public async Task Handle_WrongPictureType_ThrowsFileHasNotGroupPictureTypeException()
    {
        var adminId = 1L;
        var fileId = Guid.NewGuid();
        SetupFileClient(fileId, UploadFileType.MessageAttachmentImage, "");
        var chat = await _h.SeedChat(isGroupChat: true, title: "Group", memberUserIds: [adminId, 2]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(adminId);

        var act = async () => await handler.Handle(new UpdateGroupChatCommand { ChatId = chat.Id, PictureFileId = fileId }, CancellationToken.None);

        await act.Should().ThrowAsync<FileHasNotGroupPictureTypeException>();
    }

    [Fact]
    public async Task Handle_EmptyTitle_ThrowsGroupChatTitleIsEmptyException()
    {
        var adminId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, title: "Group", memberUserIds: [adminId, 2]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(adminId);

        var act = async () => await handler.Handle(new UpdateGroupChatCommand { ChatId = chat.Id, Title = "   " }, CancellationToken.None);

        await act.Should().ThrowAsync<GroupChatTitleIsEmptyException>();
    }

    [Fact]
    public async Task Handle_NotGroupChat_ThrowsIsNotGroupChatException()
    {
        var chat = await _h.SeedChat(isGroupChat: false, title: "DM", memberUserIds: [1, 2]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new UpdateGroupChatCommand { ChatId = chat.Id, Title = "New" }, CancellationToken.None);

        await act.Should().ThrowAsync<IsNotGroupChatException>();
    }

    [Fact]
    public async Task Handle_NoAccess_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(isGroupChat: true, title: "Group", memberUserIds: [1, 2]);
        await _h.SeedGroupChatInfo(chat.Id, 1, [1]);
        var handler = CreateHandler(99);

        var act = async () => await handler.Handle(new UpdateGroupChatCommand { ChatId = chat.Id, Title = "New" }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_NothingToChange_ReturnsCurrentWithoutSystemMessage()
    {
        var adminId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, title: "Group", memberUserIds: [adminId, 2]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(adminId);

        var result = await handler.Handle(new UpdateGroupChatCommand { ChatId = chat.Id }, CancellationToken.None);

        result.Chat.Title.Should().Be("Group");
        _h.DbContext.Messages.Should().BeEmpty();
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.NewMessageEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidUpdate_CreatesSystemMessageAndPublishes()
    {
        var adminId = 1L;
        var chat = await _h.SeedChat(isGroupChat: true, title: "Group", memberUserIds: [adminId, 2]);
        await _h.SeedGroupChatInfo(chat.Id, adminId, [adminId]);
        var handler = CreateHandler(adminId);

        await handler.Handle(new UpdateGroupChatCommand { ChatId = chat.Id, Title = "New" }, CancellationToken.None);

        _h.DbContext.Messages.ToList().Should().ContainSingle(m => m.Type == Domain.MessageContentType.System);
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.NewMessageEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
