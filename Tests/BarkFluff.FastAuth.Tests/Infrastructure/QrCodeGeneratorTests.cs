using BarkFluff.FastAuth.Infrastructure;

namespace BarkFluff.FastAuth.Tests.Infrastructure;

public class QrCodeGeneratorTests
{
    private readonly QrCodeGenerator _generator = new();

    [Fact]
    public void GeneratePngBase64_ReturnsNonEmptyString()
    {
        var result = _generator.GeneratePngBase64("test-payload");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GeneratePngBase64_ReturnsValidBase64()
    {
        var result = _generator.GeneratePngBase64("test-payload");
        var act = () => Convert.FromBase64String(result);
        act.Should().NotThrow();
    }

    [Fact]
    public void GeneratePngBase64_ReturnsPngData()
    {
        var result = _generator.GeneratePngBase64("test-payload");
        var bytes = Convert.FromBase64String(result);
        bytes.Should().NotBeEmpty();
        bytes[0].Should().Be(0x89);
        bytes[1].Should().Be(0x50);
    }

    [Fact]
    public void GeneratePngBase64_DifferentPayloads_ProduceDifferentResults()
    {
        var r1 = _generator.GeneratePngBase64("payload-1");
        var r2 = _generator.GeneratePngBase64("payload-2");
        r1.Should().NotBe(r2);
    }

    [Fact]
    public void GeneratePngBase64_SamePayload_ProducesSameResult()
    {
        var r1 = _generator.GeneratePngBase64("same-payload");
        var r2 = _generator.GeneratePngBase64("same-payload");
        r1.Should().Be(r2);
    }

    [Fact]
    public void GeneratePngBase64_LongPayload_ReturnsValidBase64()
    {
        var longPayload = new string('a', 1000);
        var result = _generator.GeneratePngBase64(longPayload);
        result.Should().NotBeNullOrEmpty();
        var act = () => Convert.FromBase64String(result);
        act.Should().NotThrow();
    }

    [Fact]
    public void GeneratePngBase64_GuidPayload_ReturnsValidBase64()
    {
        var payload = Guid.NewGuid().ToString();
        var result = _generator.GeneratePngBase64(payload);
        result.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(result);
        bytes.Should().NotBeEmpty();
    }
}
