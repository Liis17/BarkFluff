using BarkFluff.Messages.Features.Federation;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.Federation;

/// <summary>
/// Импорт снапшота вложений fed-сообщения (этап 3.1). Снапшот приходит с чужой ноды —
/// всё, что не проходит валидацию, отклоняется permanent (REJECTED), а не RETRY.
/// </summary>
public class FederatedAttachmentImporterTests
{
    private static FederatedFileRefFlat Valid(Action<FederatedFileRefFlat>? tweak = null)
    {
        var attachment = new FederatedFileRefFlat
        {
            OriginServer = "remote.test",
            FileId = Guid.NewGuid().ToString(),
            Filename = "photo.jpg",
            SizeBytes = 1024,
            AttachmentType = (int)BarkFluff.Messages.Domain.MessageAttachmentType.Image,
            PreviewFileId = Guid.NewGuid().ToString(),
            ImageWidth = 800,
            ImageHeight = 600,
        };

        tweak?.Invoke(attachment);
        return attachment;
    }

    [Fact]
    public void Import_ValidSnapshot_MapsEveryField()
    {
        var source = Valid();

        var imported = FederatedAttachmentImporter.Import([source]);

        var attachment = imported.Should().ContainSingle().Subject;
        attachment.OriginServer.Should().Be("remote.test");
        attachment.FileId.Should().Be(source.FileId);
        attachment.FileName.Should().Be("photo.jpg");
        attachment.FileSize.Should().Be(1024);
        attachment.Type.Should().Be(BarkFluff.Messages.Domain.MessageAttachmentType.Image);
        attachment.PreviewFileId.Should().Be(source.PreviewFileId);
        attachment.ImageWidth.Should().Be(800);
        attachment.ImageHeight.Should().Be(600);
        // Превью тянется с origin по требованию — локального URL у него нет.
        attachment.PreviewUrl.Should().BeNull();
    }

    [Fact]
    public void Import_EmptyOptionalFields_BecomeNull()
    {
        // 0 в proto означает «не изображение», пустая строка — «нет превью».
        var source = Valid(a =>
        {
            a.Filename = string.Empty;
            a.PreviewFileId = string.Empty;
            a.ImageWidth = 0;
            a.ImageHeight = 0;
        });

        var attachment = FederatedAttachmentImporter.Import([source]).Single();

        attachment.FileName.Should().BeNull();
        attachment.PreviewFileId.Should().BeNull();
        attachment.ImageWidth.Should().BeNull();
        attachment.ImageHeight.Should().BeNull();
    }

    [Fact]
    public void Import_EmptyList_ReturnsEmpty()
    {
        FederatedAttachmentImporter.Import([]).Should().BeEmpty();
    }

    [Fact]
    public void Import_TooManyAttachments_Throws()
    {
        var attachments = Enumerable.Range(0, FederationImportValidator.MaxAttachmentsPerMessage + 1)
            .Select(_ => Valid())
            .ToList();

        var act = () => FederatedAttachmentImporter.Import(attachments);

        act.Should().Throw<TooManyAttachmentsException>();
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void Import_MalformedFileId_Throws(string fileId)
    {
        var act = () => FederatedAttachmentImporter.Import([Valid(a => a.FileId = fileId)]);

        act.Should().Throw<FederatedAttachmentInvalidException>();
    }

    [Fact]
    public void Import_MalformedPreviewFileId_Throws()
    {
        var act = () => FederatedAttachmentImporter.Import([Valid(a => a.PreviewFileId = "nope")]);

        act.Should().Throw<FederatedAttachmentInvalidException>();
    }

    [Fact]
    public void Import_MissingOriginServer_Throws()
    {
        var act = () => FederatedAttachmentImporter.Import([Valid(a => a.OriginServer = "  ")]);

        act.Should().Throw<FederatedAttachmentInvalidException>();
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(FederationImportValidator.MaxFileBytes + 1)]
    public void Import_SizeOutOfRange_Throws(long sizeBytes)
    {
        var act = () => FederatedAttachmentImporter.Import([Valid(a => a.SizeBytes = sizeBytes)]);

        act.Should().Throw<FederatedAttachmentInvalidException>();
    }

    [Fact]
    public void Import_UnknownAttachmentType_Throws()
    {
        var act = () => FederatedAttachmentImporter.Import([Valid(a => a.AttachmentType = 999)]);

        act.Should().Throw<FederatedAttachmentInvalidException>();
    }

    [Fact]
    public void Import_TooLongFilename_Throws()
    {
        var act = () => FederatedAttachmentImporter.Import(
            [Valid(a => a.Filename = new string('a', FederatedAttachmentImporter.MaxFileNameLength + 1))]);

        act.Should().Throw<FederatedAttachmentInvalidException>();
    }

    [Fact]
    public void Import_MaxAllowedSize_IsAccepted()
    {
        var act = () => FederatedAttachmentImporter.Import(
            [Valid(a => a.SizeBytes = FederationImportValidator.MaxFileBytes)]);

        act.Should().NotThrow();
    }
}
