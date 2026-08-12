using BarkFluff.Messages.Features.Federation;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.Federation;

/// <summary>
/// Снапшот пересылки приходит с чужой ноды, поэтому проверяется целиком. Всё непрошедшее —
/// permanent-отказ: повторная доставка того же битого события ничего не исправит.
/// </summary>
public class FederatedForwardImporterTests
{
    private static FederatedForwardFlat ValidForward() => new()
    {
        AuthorName = "Remote Author",
        Text = "shared",
        Order = 0,
        OriginalSentAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    };

    private static FederatedFileRefFlat ValidAttachment() => new()
    {
        OriginServer = "partner.test",
        FileId = Guid.NewGuid().ToString(),
        SizeBytes = 1024,
        AttachmentType = (int)Domain.MessageAttachmentType.Image,
    };

    [Fact]
    public void Import_Empty_ReturnsEmpty()
    {
        FederatedForwardImporter.Import([]).Should().BeEmpty();
    }

    [Fact]
    public void Import_ValidForward_BecomesForwardedAttachment()
    {
        var forward = ValidForward();
        forward.Attachments.Add(ValidAttachment());

        var imported = FederatedForwardImporter.Import([forward]);

        imported.Should().ContainSingle();
        imported[0].Type.Should().Be(Domain.MessageAttachmentType.ForwardedMessage);
        imported[0].ForwardedAuthorName.Should().Be("Remote Author");
        imported[0].ForwardedText.Should().Be("shared");
        imported[0].ForwardedAttachments.Should().ContainSingle();

        // ID оригинала локален для origin-ноды и у нас никуда не ведёт.
        imported[0].ForwardedOriginalMessageId.Should().BeNull();
    }

    [Fact]
    public void Import_TooManyForwards_IsRejected()
    {
        var forwards = Enumerable.Range(0, 21).Select(_ => ValidForward()).ToList();

        var act = () => FederatedForwardImporter.Import(forwards);

        act.Should().Throw<FederatedForwardInvalidException>();
    }

    [Fact]
    public void Import_TextOverLimit_IsRejected()
    {
        var forward = ValidForward();
        forward.Text = new string('x', 4097);

        var act = () => FederatedForwardImporter.Import([forward]);

        act.Should().Throw<FederatedForwardInvalidException>();
    }

    [Fact]
    public void Import_AttachmentWithoutOriginServer_IsRejected()
    {
        var forward = ValidForward();
        var attachment = ValidAttachment();
        attachment.OriginServer = string.Empty;
        forward.Attachments.Add(attachment);

        var act = () => FederatedForwardImporter.Import([forward]);

        act.Should().Throw<FederatedForwardInvalidException>();
    }

    [Fact]
    public void Import_AttachmentWithNonGuidFileId_IsRejected()
    {
        var forward = ValidForward();
        var attachment = ValidAttachment();
        attachment.FileId = "not-a-guid";
        forward.Attachments.Add(attachment);

        var act = () => FederatedForwardImporter.Import([forward]);

        act.Should().Throw<FederatedForwardInvalidException>();
    }

    [Fact]
    public void Import_AttachmentWithUnknownType_IsRejected()
    {
        var forward = ValidForward();
        var attachment = ValidAttachment();
        attachment.AttachmentType = 999;
        forward.Attachments.Add(attachment);

        var act = () => FederatedForwardImporter.Import([forward]);

        act.Should().Throw<FederatedForwardInvalidException>();
    }
}
