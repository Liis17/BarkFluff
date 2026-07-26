using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.CheckFileFederationAccess;

namespace BarkFluff.Messages.Tests.Features.CheckFileFederationAccess;

/// <summary>
/// Авторизация файла на уровне НОДЫ (этап 3.2): знание file_id прав не даёт.
/// </summary>
public class CheckFileFederationAccessQueryHandlerTests
{
    private readonly TestHelper _h = new();

    private CheckFileFederationAccessQueryHandler CreateHandler() => new(_h.ChatsStorage);

    private Task<bool> CheckAsync(string fileId, string requestingServer)
        => CreateHandler()
            .Handle(
                new CheckFileFederationAccessQuery { FileId = fileId, RequestingServer = requestingServer },
                CancellationToken.None)
            .ContinueWith(t => t.Result.Allowed);

    private async Task<string> SeedAttachmentAsync(
        Chat chat,
        long senderId = 1,
        string? originServer = null)
    {
        var fileId = Guid.NewGuid().ToString();
        var message = await _h.SeedMessage(chat.Id, senderId);

        message.Content!.Attachments =
        [
            new MessageAttachment
            {
                Type = MessageAttachmentType.Document,
                FileId = fileId,
                FileSize = 1,
                OriginServer = originServer,
            }
        ];

        await _h.DbContext.SaveChangesAsync();
        return fileId;
    }

    [Fact]
    public async Task FileInFederatedChatWithRequestingServer_IsAllowed()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");
        var fileId = await SeedAttachmentAsync(chat);

        (await CheckAsync(fileId, "remote.test")).Should().BeTrue();
    }

    [Fact]
    public async Task FileInFederatedChatWithAnotherServer_IsDenied()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "other.test");
        var fileId = await SeedAttachmentAsync(chat);

        (await CheckAsync(fileId, "remote.test")).Should().BeFalse();
    }

    [Fact]
    public async Task FileOnlyInLocalChat_IsDenied()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var fileId = await SeedAttachmentAsync(chat);

        (await CheckAsync(fileId, "remote.test")).Should().BeFalse();
    }

    [Fact]
    public async Task UnknownFile_IsDenied()
    {
        await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");

        (await CheckAsync(Guid.NewGuid().ToString(), "remote.test")).Should().BeFalse();
    }

    [Fact]
    public async Task RemoteAttachment_IsNotReExported()
    {
        // Файл, пришедший с чужой ноды, мы не реэкспортируем — за ним следует идти на его origin.
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");
        var fileId = await SeedAttachmentAsync(chat, originServer: "third.test");

        (await CheckAsync(fileId, "remote.test")).Should().BeFalse();
    }

    [Theory]
    [InlineData(FederatedStatus.Rejected)]
    [InlineData(FederatedStatus.Merged)]
    public async Task NonActiveFederatedChat_IsDenied(FederatedStatus status)
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test", status);
        var fileId = await SeedAttachmentAsync(chat);

        (await CheckAsync(fileId, "remote.test")).Should().BeFalse();
    }

    [Fact]
    public async Task DeletedMessage_IsDenied()
    {
        // После репликации delete (2.4) партнёр за таким файлом и не придёт.
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");
        var fileId = Guid.NewGuid().ToString();
        var message = await _h.SeedMessage(chat.Id, 1, isDeleted: true);

        message.Content!.Attachments =
        [
            new MessageAttachment { Type = MessageAttachmentType.Document, FileId = fileId, FileSize = 1 }
        ];
        await _h.DbContext.SaveChangesAsync();

        (await CheckAsync(fileId, "remote.test")).Should().BeFalse();
    }

    [Fact]
    public async Task ServerNameCaseInsensitive_IsAllowed()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");
        var fileId = await SeedAttachmentAsync(chat);

        (await CheckAsync(fileId, "  Remote.TEST  ")).Should().BeTrue();
    }

    [Fact]
    public async Task MalformedFileId_IsDeniedWithoutQuery()
    {
        (await CheckAsync("not-a-guid", "remote.test")).Should().BeFalse();
    }

    [Fact]
    public async Task EmptyRequestingServer_IsDenied()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");
        var fileId = await SeedAttachmentAsync(chat);

        (await CheckAsync(fileId, "   ")).Should().BeFalse();
    }
}
