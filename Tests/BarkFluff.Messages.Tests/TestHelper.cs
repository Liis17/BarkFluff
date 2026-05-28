using System.Security.Claims;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.Tests;

public class TestHelper
{
    private static long _nextId = 1;

    public MessagesContext DbContext { get; }
    public ChatsStorage ChatsStorage { get; }
    public MessagesStorage MessagesStorage { get; }
    public PinnedMessagesStorage PinnedMessagesStorage { get; }
    public EncryptedMessagesStorage EncryptedMessagesStorage { get; }
    public Mock<IPublishEndpoint> PublishEndpointMock { get; }
    public MetricsCollector Metrics { get; }

    public TestHelper()
    {
        var options = new DbContextOptionsBuilder<MessagesContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        DbContext = new MessagesContext(options);
        ChatsStorage = new ChatsStorage(DbContext);
        MessagesStorage = new MessagesStorage(DbContext, ChatsStorage);
        PinnedMessagesStorage = new PinnedMessagesStorage(DbContext);
        EncryptedMessagesStorage = new EncryptedMessagesStorage(DbContext);
        PublishEndpointMock = new Mock<IPublishEndpoint>();
        Metrics = new MetricsCollector();
    }

    public UserContext CreateUserContext(long userId, string? deviceId = null)
    {
        var claims = new List<Claim>
        {
            new(IdentityClaims.UserId, userId.ToString()),
            new(IdentityClaims.TokenType, "User"),
        };
        if (deviceId != null)
            claims.Add(new Claim(IdentityClaims.DeviceId, deviceId));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        return new UserContext(httpContextAccessor.Object);
    }

    public static ILogger<T> CreateLogger<T>()
    {
        return Mock.Of<ILogger<T>>();
    }

    public async Task<Chat> SeedChat(
        bool isGroupChat = false,
        string? title = null,
        ChatType type = ChatType.Regular,
        List<long>? memberUserIds = null,
        byte[]? kdfSalt = null,
        byte[]? passphraseVerifier = null)
    {
        var chat = new Chat
        {
            IsGroupChat = isGroupChat,
            Title = title,
            Type = type,
            KdfSalt = kdfSalt,
            PassphraseVerifier = passphraseVerifier,
            Members = memberUserIds?.Select(uid => new ChatMember
            {
                UserId = uid,
                JoinedAt = DateTime.UtcNow
            }).ToList() ?? new List<ChatMember>()
        };

        DbContext.Chats.Add(chat);
        await DbContext.SaveChangesAsync();
        return chat;
    }

    public async Task<Message> SeedMessage(
        Guid chatId,
        long senderId,
        string? text = "test message",
        MessageContentType type = MessageContentType.Generic,
        List<long>? readBy = null,
        bool isDeleted = false,
        bool isEdited = false)
    {
        var message = new Message
        {
            Id = Interlocked.Increment(ref _nextId),
            ChatId = chatId,
            SenderId = senderId,
            SentAt = DateTime.UtcNow,
            Type = type,
            IsDeleted = isDeleted,
            IsEdited = isEdited,
            ReadBy = readBy ?? [senderId],
            Content = new MessageContent
            {
                Text = text,
                Attachments = new List<MessageAttachment>()
            }
        };

        DbContext.Messages.Add(message);
        await DbContext.SaveChangesAsync();
        return message;
    }

    public async Task<EncryptedMessage> SeedEncryptedMessage(
        Guid chatId,
        long senderId,
        Guid senderDeviceId,
        byte[]? ciphertext = null,
        bool isDeleted = false)
    {
        var message = new EncryptedMessage
        {
            ChatId = chatId,
            SenderId = senderId,
            SenderDeviceId = senderDeviceId,
            SentAt = DateTime.UtcNow,
            Ciphertext = ciphertext ?? new byte[32],
            Nonce = new byte[12],
            AssociatedData = Array.Empty<byte>(),
            IsDeleted = isDeleted
        };

        DbContext.EncryptedMessages.Add(message);
        await DbContext.SaveChangesAsync();
        return message;
    }

    public async Task<PinnedMessage> SeedPinnedMessage(
        Guid chatId,
        long messageId,
        long pinnerUserId)
    {
        var pin = new PinnedMessage
        {
            ChatId = chatId,
            MessageId = messageId,
            PinnerUserId = pinnerUserId,
            PinnedAt = DateTime.UtcNow
        };

        DbContext.PinnedMessages.Add(pin);
        await DbContext.SaveChangesAsync();
        return pin;
    }

    public async Task<GroupChatInfo> SeedGroupChatInfo(
        Guid chatId,
        long creatorId,
        List<long>? usersCanKick = null)
    {
        var info = new GroupChatInfo
        {
            ChatId = chatId,
            Creator = creatorId,
            UsersCanKick = usersCanKick ?? [creatorId],
            CreatedAt = DateTime.UtcNow
        };

        DbContext.GroupChatInfos.Add(info);
        await DbContext.SaveChangesAsync();
        return info;
    }
}
