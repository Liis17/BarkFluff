using BarkFluff.Notification.Helpers;

namespace BarkFluff.Notification.Tests.Helpers;

public class EmailMaskerTests
{
    [Fact]
    public void Mask_WithValidEmail_ReturnsMaskedWithDomain()
    {
        var result = EmailMasker.Mask("user@example.com");

        result.Should().Be("***@example.com");
    }

    [Fact]
    public void Mask_WithSubdomainEmail_ReturnsMaskedWithFullDomain()
    {
        var result = EmailMasker.Mask("admin@mail.company.ru");

        result.Should().Be("***@mail.company.ru");
    }

    [Fact]
    public void Mask_WithNullEmail_ReturnsAsterisks()
    {
        var result = EmailMasker.Mask(null!);

        result.Should().Be("***");
    }

    [Fact]
    public void Mask_WithEmptyString_ReturnsAsterisks()
    {
        var result = EmailMasker.Mask(string.Empty);

        result.Should().Be("***");
    }

    [Fact]
    public void Mask_WithEmailWithoutDomain_ReturnsAsterisks()
    {
        var result = EmailMasker.Mask("noemail");

        result.Should().Be("***");
    }

    [Fact]
    public void Mask_WithAtSignOnly_ReturnsAsterisks()
    {
        var result = EmailMasker.Mask("@");

        result.Should().Be("***");
    }

    [Fact]
    public void Mask_WithAtAtStart_ReturnsAsterisks()
    {
        var result = EmailMasker.Mask("@domain.com");

        result.Should().Be("***");
    }

    [Fact]
    public void Mask_WithAtInMiddle_ReturnsMasked()
    {
        var result = EmailMasker.Mask("a@b");

        result.Should().Be("***@b");
    }

    [Fact]
    public void Mask_DoesNotLeakLocalPart()
    {
        var result = EmailMasker.Mask("verysecretuser@private.org");

        result.Should().NotContain("verysecretuser");
    }

    [Fact]
    public void Mask_PreservesDomainPart()
    {
        var result = EmailMasker.Mask("test@my.domain.com");

        result.Should().Contain("my.domain.com");
    }

    [Fact]
    public void Mask_WithWhitespaceOnly_ReturnsAsterisks()
    {
        var result = EmailMasker.Mask("   ");

        result.Should().Be("***");
    }

    [Fact]
    public void Mask_WithUnicodeEmail_ReturnsMaskedWithDomain()
    {
        var result = EmailMasker.Mask("user@пример.рф");

        result.Should().Be("***@пример.рф");
    }

    [Fact]
    public void Mask_WithMultipleAtSigns_MasksOnlyUpToFirstAt()
    {
        var result = EmailMasker.Mask("user@domain@extra.com");

        result.Should().Be("***@domain@extra.com");
    }

    [Fact]
    public void Mask_VeryLongEmail_StillMasks()
    {
        var result = EmailMasker.Mask("a-very-long-local-part-that-goes-on-and-on@example.com");

        result.Should().Be("***@example.com");
        result.Should().NotContain("a-very-long-local-part");
    }

    [Fact]
    public void Mask_SingleCharLocalPart_MasksIt()
    {
        var result = EmailMasker.Mask("a@b.com");

        result.Should().Be("***@b.com");
        result.Should().NotContain("a");
    }
}
