using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.SendMessage;
using BarkFluff.Messages.Infrastructure;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Files;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

using OutgoingMsg = BarkFluff.Messages.Features.SendMessage.OutgoingMessage;

namespace BarkFluff.Messages.Tests.Features.SendMessage;

public class SendMessageCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;
    private readonly Mock<FilesServerApi.FilesServerApiClient> _filesClient;
    private readonly ChatCache _chatCache;
    private readonly MessageQueueSender _queueSender;

    public SendMessageCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        _filesClient = new Mock<FilesServerApi.FilesServerApiClient>();
        var cacheMock = new Mock<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
        _chatCache = new ChatCache(cacheMock.Object, TestHelper.CreateLogger<ChatCache>());
        _queueSender = new MessageQueueSender(_h.PublishEndpointMock.Object);
    }

    private SendMessageCommandHandler CreateHandler(long userId)
    {
        return new SendMessageCommandHandler(
            _h.ChatsStorage,
            _usersClient.Object,
            _h.CreateUserContext(userId),
            _filesClient.Object,
            _chatCache,
            _h.MessagesStorage,
            _queueSender,
            TestHelper.CreateConfiguration(),
            _h.Metrics,
            _h.CreateReplyPreviewResolver(),
            TestHelper.CreateLogger<SendMessageCommandHandler>());
    }

    private void SetupUsersClient(long userId, string firstName = "Test", string lastName = "User", string username = "testuser")
    {
        var response = new GetByIdResponse
        {
            User = new User { Id = userId, FirstName = firstName, LastName = lastName, Username = username }
        };
        _usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetByIdResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    [Fact]
    public async Task Handle_ValidTextMessage_SendsAndReturnsResponse()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { Text = "Hello" }
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.Message.Content.Text.Should().Be("Hello");
        _h.PublishEndpointMock.Verify(p => p.Publish(It.IsAny<Shared.Queue.Messages.NewMessageEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NullMessage_ThrowsMessageNotContainContextException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendMessageCommand { ChatId = Guid.NewGuid(), Message = null }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotContainContextException>();
    }

    [Fact]
    public async Task Handle_EmptyMessage_ThrowsMessageNotContainContextException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendMessageCommand
        {
            ChatId = Guid.NewGuid(),
            Message = new OutgoingMsg()
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageNotContainContextException>();
    }

    [Fact]
    public async Task Handle_TextTooLong_ThrowsMessageTextTooLongException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendMessageCommand
        {
            ChatId = Guid.NewGuid(),
            Message = new OutgoingMsg { Text = new string('a', 4097) }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<MessageTextTooLongException>();
    }

    [Fact]
    public async Task Handle_TextExactlyMax_DoesNotThrow()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { Text = new string('a', 4096) }
        }, CancellationToken.None);

        await act.Should().NotThrowAsync<MessageTextTooLongException>();
    }

    [Fact]
    public async Task Handle_TooManyAttachments_ThrowsTooManyAttachmentsException()
    {
        var handler = CreateHandler(1);
        var fileIds = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToList();

        var act = async () => await handler.Handle(new SendMessageCommand
        {
            ChatId = Guid.NewGuid(),
            Message = new OutgoingMsg { FileIds = fileIds }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<TooManyAttachmentsException>();
    }

    [Fact]
    public async Task Handle_NoChatIdNoUserId_ThrowsSourceForSendMessageNotSetException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendMessageCommand
        {
            Message = new OutgoingMsg { Text = "hello" }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<SourceForSendMessageNotSetException>();
    }

    [Fact]
    public async Task Handle_NoAccessToChat_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(memberUserIds: [99, 100]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { Text = "hello" }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_UserIdCreatesNewChat_CreatesPersonChat()
    {
        SetupUsersClient(2);
        SetupUsersClient(1);
        var handler = CreateHandler(1);

        var result = await handler.Handle(new SendMessageCommand
        {
            UserId = 2,
            Message = new OutgoingMsg { Text = "hello" }
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result.Message.Content.Text.Should().Be("hello");
    }

    [Fact]
    public async Task Handle_MaxAttachmentsAllowed_DoesNotThrow()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var fileIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        var filesInfoResponse = new GetFilesDataResponse();
        foreach (var fid in fileIds)
        {
            filesInfoResponse.FilesInfos.Add(new UploadFileInfo
            {
                Id = fid.ToString(),
                Type = UploadFileType.MessageAttachmentImage,
                FileSize = 100
            });
        }

        _filesClient.Setup(c => c.GetFilesDataAsync(It.IsAny<GetFilesDataRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetFilesDataResponse>(
                Task.FromResult(filesInfoResponse),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { FileIds = fileIds }
        }, CancellationToken.None);

        await act.Should().NotThrowAsync<TooManyAttachmentsException>();
    }

    [Fact]
    public async Task Handle_ExistingFederatedChatViaChatId_MarksMessageFederated()
    {
        // Отправка последующих сообщений в уже существующий fed-DM через chat_id (docs/rearch/05,
        // «Отправка последующих сообщений») — не только через явный user_uuid первого сообщения.
        var userId = 1L;
        var localUuid = Guid.NewGuid();
        var remoteUuid = Guid.NewGuid();
        var chat = await _h.SeedFederatedChat(userId, localUuid, remoteUuid, "remote.test");
        SetupUsersClient(userId, username: "alice");
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { Text = "second message" }
        }, CancellationToken.None);

        result.Message.FederatedId.Should().NotBeNullOrEmpty();
        result.Message.SenderUuid.Should().Be(localUuid.ToString());

        _h.PublishEndpointMock.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.NewMessageEvent>(e =>
                e.IsFederated
                && e.RemoteParticipants.Count == 1
                && e.RemoteParticipants[0].Uuid == remoteUuid
                && e.SenderUuid == localUuid
                && e.SenderFid == "@alice:remote.test"
                && e.LastChangeAt.HasValue),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RejectedFederatedChat_ThrowsFederatedDmRejectedException()
    {
        // Этап 2.5: чат отклонён (invitee запретил fed-DM) — понятная ошибка вместо тихой отправки
        // в чат, который вторая сторона никогда не увидит.
        var userId = 1L;
        var chat = await _h.SeedFederatedChat(
            userId, Guid.NewGuid(), Guid.NewGuid(), "remote.test", Domain.FederatedStatus.Rejected);
        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { Text = "hello" }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<FederatedDmRejectedException>();
    }

    [Fact]
    public async Task Handle_ConcurrentFederatedChatCreation_AdoptsWinnerChat()
    {
        // Баг #9a: два параллельных SendMessage(user_uuid=X) для нового fed-DM — CreateFederatedChatAsync
        // под конфликтом уникального индекса пары внутренне возвращает чат ПОБЕДИТЕЛЯ гонки (другой Id).
        // Обработчик обязан принять его, а не продолжать со своим локально сгенерированным chatId
        // (иначе сообщение осиротеет — Message.ChatId без реального Chat/ChatMember).
        var userId = 1L;
        var localUuid = Guid.NewGuid();
        var targetUuid = Guid.NewGuid();

        _usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetByIdResponse>(
                Task.FromResult(new GetByIdResponse { User = new User { Id = userId, Username = "alice", Uuid = localUuid.ToString() } }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));
        _usersClient.Setup(c => c.GetUsersByUuidAsync(It.IsAny<GetUsersByUuidRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetUsersByUuidResponse>(
                Task.FromResult(new GetUsersByUuidResponse
                {
                    Users = { new UserProfileByUuid { Uuid = targetUuid.ToString(), Found = true, IsRemote = true, ServerName = "remote.test" } },
                }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        // "Победитель" гонки — реальный persisted чат с ДРУГОЙ uuid-парой (чтобы pre-check
        // FindActiveFederatedChatByUuidPairAsync для настоящей пары localUuid/targetUuid его не находил
        // и код действительно дошёл до CreateFederatedChatAsync), но с валидным членством отправителя.
        var winnerChat = await _h.SeedFederatedChat(userId, localUuid, Guid.NewGuid(), "other.test");

        var chatsStorageMock = new Mock<ChatsStorage>(_h.DbContext) { CallBase = true };
        chatsStorageMock
            .Setup(s => s.CreateFederatedChatAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(winnerChat);

        var handler = new SendMessageCommandHandler(
            chatsStorageMock.Object,
            _usersClient.Object,
            _h.CreateUserContext(userId),
            _filesClient.Object,
            _chatCache,
            _h.MessagesStorage,
            _queueSender,
            TestHelper.CreateConfiguration(),
            _h.Metrics,
            _h.CreateReplyPreviewResolver(),
            TestHelper.CreateLogger<SendMessageCommandHandler>());

        await handler.Handle(new SendMessageCommand
        {
            UserUuid = targetUuid,
            Message = new OutgoingMsg { Text = "race" }
        }, CancellationToken.None);

        _h.DbContext.Messages.Should().ContainSingle(m => m.ChatId == winnerChat.Id);
        _h.PublishEndpointMock.Verify(p => p.Publish(
            It.Is<Shared.Queue.Messages.NewMessageEvent>(e =>
                e.ChatId == winnerChat.Id
                && !e.IsFirstMessageInChat
                && e.InitiatorUuid == null
                && e.InviteeUuid == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnsupportedFileType_ThrowsFileNotSupportedException()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var fileId = Guid.NewGuid();

        var filesInfoResponse = new GetFilesDataResponse
        {
            FilesInfos = { new UploadFileInfo { Id = fileId.ToString(), Type = (UploadFileType)999 } }
        };

        _filesClient.Setup(c => c.GetFilesDataAsync(It.IsAny<GetFilesDataRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetFilesDataResponse>(
                Task.FromResult(filesInfoResponse),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var handler = CreateHandler(userId);

        var act = async () => await handler.Handle(new SendMessageCommand
        {
            ChatId = chat.Id,
            Message = new OutgoingMsg { FileIds = [fileId] }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<FileNotSupportedException>();
    }
}
