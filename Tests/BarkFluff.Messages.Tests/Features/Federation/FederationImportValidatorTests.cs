using BarkFluff.Messages.Features.Federation;
using BarkFluff.Shared.Exceptions.Messages;

using FluentAssertions;

using Xunit;

namespace BarkFluff.Messages.Tests.Features.Federation;

public class FederationImportValidatorTests
{
    [Fact]
    public void ClampOriginTs_AcceptsRecentTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeMilliseconds();

        var result = FederationImportValidator.ClampOriginTs(ts);

        result.Should().BeCloseTo(now.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ClampOriginTs_AcceptsSlightlyFutureTimestamp_WithinWindow()
    {
        var future = DateTimeOffset.UtcNow.AddSeconds(FederationImportValidator.TimestampFutureWindowSeconds - 10);
        var ts = future.ToUnixTimeMilliseconds();

        var act = () => FederationImportValidator.ClampOriginTs(ts);

        act.Should().NotThrow();
    }

    [Fact]
    public void ClampOriginTs_RejectsFarFutureTimestamp()
    {
        var far = DateTimeOffset.UtcNow.AddHours(1);
        var ts = far.ToUnixTimeMilliseconds();

        var act = () => FederationImportValidator.ClampOriginTs(ts);

        act.Should().Throw<TimestampInFutureException>();
    }

    [Fact]
    public void ClampOriginTs_RejectsZeroTimestamp()
    {
        var act = () => FederationImportValidator.ClampOriginTs(0);
        act.Should().Throw<TimestampInFutureException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(FederationImportValidator.MaxTextLength)]
    public void ValidateText_AcceptsUpToLimit(int len)
    {
        var text = new string('a', len);
        var act = () => FederationImportValidator.ValidateText(text);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateText_RejectsAboveLimit()
    {
        var text = new string('a', FederationImportValidator.MaxTextLength + 1);
        var act = () => FederationImportValidator.ValidateText(text);
        act.Should().Throw<MessageTextTooLongException>();
    }

    [Fact]
    public void ValidateAttachmentCount_RejectsAboveLimit()
    {
        var act = () => FederationImportValidator.ValidateAttachmentCount(FederationImportValidator.MaxAttachmentsPerMessage + 1);
        act.Should().Throw<TooManyAttachmentsException>();
    }
}

public class FederatedUuidPairTests
{
    [Fact]
    public void Normalize_IsDeterministic_SymmetricBothSides()
    {
        // docs/rearch/05, README фазы 2: обе ноды вычисляют одного победителя для одной пары.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var ab = FederatedUuidPair.Normalize(a, b);
        var ba = FederatedUuidPair.Normalize(b, a);

        ab.Should().Be(ba);
        ab.Low.Should().Be(ba.Low);
        ab.High.Should().Be(ba.High);
    }

    [Fact]
    public void Normalize_LowIsLexicographicallyFirst()
    {
        // "D"-lowercase ordinal: на любой платформе одинаково для одной и той же Guid-пары.
        var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var result = FederatedUuidPair.Normalize(a, b);

        result.Low.Should().Be(a);
        result.High.Should().Be(b);
    }
}
