using System.Security.Claims;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.Federation;
using BarkFluff.Messages.Persistence;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BarkFluff.Messages.Tests;

public class TestHelper
{
    private static long _nextId = 1;

    public MessagesContext DbContext { get; }
    public ChatsStorage ChatsStorage { get; }
    public MessagesStorage MessagesStorage { get; }
    public PinnedMessagesStorage PinnedMessagesStorage { get; }
    public EncryptedMessagesStorage EncryptedMessagesStorage { get; }
    public FederatedReadStatesStorage FederatedReadStatesStorage { get; }
    public ChatDraftsStorage ChatDraftsStorage { get; }
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
        FederatedReadStatesStorage = new FederatedReadStatesStorage(DbContext);
        ChatDraftsStorage = new ChatDraftsStorage(DbContext);
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

    public static IConfiguration CreateConfiguration(string serverName = "home.test")
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Federation:ServerName"] = serverName })
            .Build();
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
        bool isEdited = false,
        DateTime? sentAt = null)
    {
        var message = new Message
        {
            Id = Interlocked.Increment(ref _nextId),
            ChatId = chatId,
            SenderId = senderId,
            SentAt = sentAt ?? DateTime.UtcNow,
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

    // Fed-DM (этапы 2.3/2.4): один локальный участник + один remote. Используется тестами
    // ApplyFederatedEdit/Delete/Read и LWW-сценариев.
    public async Task<Chat> SeedFederatedChat(
        long localUserId,
        Guid localUserUuid,
        Guid remoteUuid,
        string remoteServerName,
        FederatedStatus status = FederatedStatus.Active)
    {
        var (low, high) = FederatedUuidPair.Normalize(localUserUuid, remoteUuid);
        var chat = new Chat
        {
            IsGroupChat = false,
            Type = ChatType.Regular,
            IsFederated = true,
            FederatedStatus = status,
            FederatedUuidLow = low,
            FederatedUuidHigh = high,
            Members = new List<ChatMember>
            {
                new() { UserId = localUserId, UserUuid = localUserUuid, JoinedAt = DateTime.UtcNow },
                new() { UserId = null, UserUuid = remoteUuid, ServerName = remoteServerName, JoinedAt = DateTime.UtcNow },
            },
        };

        DbContext.Chats.Add(chat);
        await DbContext.SaveChangesAsync();
        return chat;
    }

    public async Task<Message> SeedFederatedMessage(
        Guid chatId,
        Guid federatedId,
        Guid? senderUuid,
        long? senderId = null,
        string? text = "test",
        DateTime? lastChangeAt = null,
        bool isDeleted = false)
    {
        var changeAt = lastChangeAt ?? DateTime.UtcNow;
        var message = new Message
        {
            Id = Interlocked.Increment(ref _nextId),
            ChatId = chatId,
            SenderId = senderId,
            SenderUuid = senderUuid,
            FederatedId = federatedId,
            SentAt = changeAt,
            LastChangeAt = changeAt,
            IsDeleted = isDeleted,
            Type = MessageContentType.Generic,
            ReadBy = new List<long>(),
            Content = new MessageContent { Text = text, Attachments = new List<MessageAttachment>() },
        };

        DbContext.Messages.Add(message);
        await DbContext.SaveChangesAsync();
        return message;
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
