using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.CheckFedFileUserAccess;
using BarkFluff.Proto.Messages;

using Chat = BarkFluff.Messages.Domain.Chat;

namespace BarkFluff.Messages.Tests.Features.CheckFedFileUserAccess;

/// <summary>
/// Авторизация federated-файла на уровне ПОЛЬЗОВАТЕЛЯ (этап 3.3) — второй уровень,
/// независимый от решения origin.
/// </summary>
public class CheckFedFileUserAccessQueryHandlerTests
{
    private const string Origin = "remote.test";

    private readonly TestHelper _h = new();

    private Task<CheckFedFileUserAccessResponse> CheckAsync(long userId, string fileId, string origin = Origin)
        => new CheckFedFileUserAccessQueryHandler(_h.ChatsStorage).Handle(
            new CheckFedFileUserAccessQuery { UserId = userId, OriginServer = origin, FileId = fileId },
            CancellationToken.None);

    private async Task<string> SeedFederatedAttachmentAsync(
        Chat chat,
        string? originServer = Origin,
        string? fileName = "doc.pdf",
        long size = 4096)
    {
        var fileId = Guid.NewGuid().ToString();
        var message = await _h.SeedMessage(chat.Id, 1);

        message.Content!.Attachments =
        [
            new MessageAttachment
            {
                Type = MessageAttachmentType.Document,
                FileId = fileId,
                FileSize = size,
                OriginServer = originServer,
                FileName = fileName,
            }
        ];

        await _h.DbContext.SaveChangesAsync();
        return fileId;
    }

    [Fact]
    public async Task ChatMember_IsAllowedAndGetsSnapshot()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), Origin);
        var fileId = await SeedFederatedAttachmentAsync(chat);

        var response = await CheckAsync(1, fileId);

        response.Allowed.Should().BeTrue();
        response.FileName.Should().Be("doc.pdf");
        response.SizeBytes.Should().Be(4096);
        response.AttachmentType.Should().Be((int)MessageAttachmentType.Document);
    }

    [Fact]
    public async Task NonMember_IsDenied()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), Origin);
        var fileId = await SeedFederatedAttachmentAsync(chat);

        (await CheckAsync(999, fileId)).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task LocalAttachmentWithSameFileId_DoesNotMatch()
    {
        // Совпадение file_id с локальным файлом не должно давать доступ по fed-ветке:
        // сопоставление идёт по паре (origin_server, file_id).
        var chat = await _h.SeedChat(memberUserIds: [1]);
        var fileId = await SeedFederatedAttachmentAsync(chat, originServer: null);

        (await CheckAsync(1, fileId)).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task DifferentOriginServer_DoesNotMatch()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), Origin);
        var fileId = await SeedFederatedAttachmentAsync(chat);

        (await CheckAsync(1, fileId, origin: "other.test")).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task ForwardedAttachment_IsAllowed()
    {
        // Форварднувший пользователь легитимно видел вложение — получатель форварда
        // должен уметь его открыть.
        var chat = await _h.SeedChat(memberUserIds: [1]);
        var fileId = Guid.NewGuid().ToString();
        var message = await _h.SeedMessage(chat.Id, 2);

        message.Content!.Attachments =
        [
            new MessageAttachment
            {
                Type = MessageAttachmentType.ForwardedMessage,
                FileId = string.Empty,
                ForwardedAuthorName = "Alice",
                ForwardedAttachments =
                [
                    new ForwardedMessageAttachment
                    {
                        Type = MessageAttachmentType.Image,
                        FileId = fileId,
                        FileSize = 512,
                        OriginServer = Origin,
                    }
                ],
            }
        ];

        await _h.DbContext.SaveChangesAsync();

        var response = await CheckAsync(1, fileId);

        response.Allowed.Should().BeTrue();
        response.SizeBytes.Should().Be(512);
    }

    [Fact]
    public async Task DeletedMessage_IsDenied()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), Origin);
        var fileId = Guid.NewGuid().ToString();
        var message = await _h.SeedMessage(chat.Id, 1, isDeleted: true);

        message.Content!.Attachments =
        [
            new MessageAttachment
            {
                Type = MessageAttachmentType.Document,
                FileId = fileId,
                OriginServer = Origin,
            }
        ];
        await _h.DbContext.SaveChangesAsync();

        (await CheckAsync(1, fileId)).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task UnknownFile_IsDenied()
    {
        (await CheckAsync(1, Guid.NewGuid().ToString())).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task MalformedFileId_IsDenied()
    {
        (await CheckAsync(1, "not-a-guid")).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task OriginServerCaseInsensitive_IsAllowed()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), Origin);
        var fileId = await SeedFederatedAttachmentAsync(chat);

        (await CheckAsync(1, fileId, origin: "  Remote.TEST ")).Allowed.Should().BeTrue();
    }
}
