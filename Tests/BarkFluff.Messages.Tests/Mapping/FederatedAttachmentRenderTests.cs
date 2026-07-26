using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.Federation;
using BarkFluff.Messages.Mapping;
using BarkFluff.Proto.Files;

namespace BarkFluff.Messages.Tests.Mapping;

/// <summary>
/// Рендер federated-вложения из снапшота (этап 3.1): без Files, без похода на origin.
/// </summary>
public class FederatedAttachmentRenderTests
{
    private static MessageContent WithAttachment(MessageAttachment attachment)
        => new() { Text = "hi", Attachments = [attachment] };

    [Fact]
    public void ToGrpc_FederatedAttachment_RendersFromSnapshotWithoutFilesInfo()
    {
        var fileId = Guid.NewGuid().ToString();
        var previewId = Guid.NewGuid().ToString();

        var content = WithAttachment(new MessageAttachment
        {
            Type = MessageAttachmentType.Image,
            FileId = fileId,
            FileSize = 2048,
            OriginServer = "remote.test",
            FileName = "cat.png",
            PreviewFileId = previewId,
            ImageWidth = 1280,
            ImageHeight = 720,
        });

        // filesInfoMap = null: Files не спрашивали вовсе — файла на этой ноде нет.
        var proto = content.ToGrpc(null);

        var attachment = proto.Attachments.Should().ContainSingle().Subject;
        attachment.FileId.Should().Be(fileId);
        attachment.OriginServer.Should().Be("remote.test");
        attachment.FileName.Should().Be("cat.png");
        attachment.AttachmentSize.Should().Be(2048);
        attachment.PreviewFileId.Should().Be(previewId);
        attachment.ImageWidth.Should().Be(1280);
        attachment.ImageHeight.Should().Be(720);
        // Контракт ссылки на превью — этап 3.3; здесь заполнять нечем.
        attachment.PreviewUrl.Should().BeEmpty();
    }

    [Fact]
    public void ToGrpc_FederatedAttachment_IgnoresFilesInfoEvenIfPresent()
    {
        // Снапшот — источник истины для remote: случайное совпадение file_id с локальным
        // файлом не должно подменить имя.
        var fileId = Guid.NewGuid().ToString();
        var content = WithAttachment(new MessageAttachment
        {
            Type = MessageAttachmentType.Document,
            FileId = fileId,
            OriginServer = "remote.test",
            FileName = "from-snapshot.pdf",
        });

        var filesInfo = new Dictionary<string, UploadFileInfo>
        {
            [fileId] = new() { Id = fileId, FileName = "from-files.pdf" },
        };

        var attachment = content.ToGrpc(filesInfo).Attachments.Single();

        attachment.FileName.Should().Be("from-snapshot.pdf");
    }

    [Fact]
    public void ToGrpc_LocalAttachment_StillUsesFilesInfo()
    {
        // Регрессия: локальный рендер не изменился.
        var fileId = Guid.NewGuid().ToString();
        var content = WithAttachment(new MessageAttachment
        {
            Type = MessageAttachmentType.Document,
            FileId = fileId,
            FileSize = 10,
        });

        var filesInfo = new Dictionary<string, UploadFileInfo>
        {
            [fileId] = new() { Id = fileId, FileName = "local.pdf", PreviewFileId = "prev" },
        };

        var attachment = content.ToGrpc(filesInfo).Attachments.Single();

        attachment.FileName.Should().Be("local.pdf");
        attachment.PreviewFileId.Should().Be("prev");
        attachment.OriginServer.Should().BeEmpty();
    }

    [Fact]
    public void Build_OutgoingSnapshot_UsesOwnServerAndFilesMetadata()
    {
        var fileId = Guid.NewGuid().ToString();
        var attachments = new List<MessageAttachment>
        {
            new() { Type = MessageAttachmentType.Image, FileId = fileId, FileSize = 512 },
        };

        var filesInfo = new Dictionary<string, UploadFileInfo>
        {
            [fileId] = new()
            {
                Id = fileId,
                FileName = "pic.jpg",
                PreviewFileId = "preview-1",
                ImageWidth = 100,
                ImageHeight = 50,
            },
        };

        var refs = FederatedAttachmentMapper.Build(attachments, filesInfo, "home.test");

        var reference = refs.Should().ContainSingle().Subject;
        reference.OriginServer.Should().Be("home.test");
        reference.FileId.Should().Be(fileId);
        reference.FileName.Should().Be("pic.jpg");
        reference.SizeBytes.Should().Be(512);
        reference.PreviewFileId.Should().Be("preview-1");
        reference.ImageWidth.Should().Be(100);
        reference.ImageHeight.Should().Be(50);
    }

    [Fact]
    public void Build_ForwardedAttachment_IsSkipped()
    {
        // Forward-структура федерируется внутри самого сообщения, отдельным файловым ref'ом не является.
        var attachments = new List<MessageAttachment>
        {
            new() { Type = MessageAttachmentType.ForwardedMessage, FileId = string.Empty },
        };

        FederatedAttachmentMapper.Build(attachments, null, "home.test").Should().BeNull();
    }

    [Fact]
    public void Build_WithoutOwnServerName_ReturnsNull()
    {
        // Нода без Federation:ServerName ничего не федерирует.
        var attachments = new List<MessageAttachment>
        {
            new() { Type = MessageAttachmentType.Image, FileId = Guid.NewGuid().ToString() },
        };

        FederatedAttachmentMapper.Build(attachments, null, string.Empty).Should().BeNull();
    }
}
