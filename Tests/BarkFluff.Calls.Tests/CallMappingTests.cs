using BarkFluff.Calls.Domain;
using BarkFluff.Calls.Services;

using ProtoQuality = BarkFluff.Proto.Calls.CallAudioQuality;

namespace BarkFluff.Calls.Tests;

public class CallMappingTests
{
    [Theory]
    [InlineData(ProtoQuality.Auto, CallAudioQualityKind.Auto)]
    [InlineData(ProtoQuality.Low, CallAudioQualityKind.Low)]
    [InlineData(ProtoQuality.Medium, CallAudioQualityKind.Medium)]
    [InlineData(ProtoQuality.High, CallAudioQualityKind.High)]
    public void ToDomain_MapsEachQuality(ProtoQuality proto, CallAudioQualityKind expected)
        => proto.ToDomain().Should().Be(expected);

    [Theory]
    [InlineData(CallAudioQualityKind.Auto, ProtoQuality.Auto)]
    [InlineData(CallAudioQualityKind.Low, ProtoQuality.Low)]
    [InlineData(CallAudioQualityKind.Medium, ProtoQuality.Medium)]
    [InlineData(CallAudioQualityKind.High, ProtoQuality.High)]
    public void ToProto_MapsEachQuality(CallAudioQualityKind domain, ProtoQuality expected)
        => domain.ToProto().Should().Be(expected);

    [Theory]
    [InlineData(CallAudioQualityKind.Auto)]
    [InlineData(CallAudioQualityKind.Low)]
    [InlineData(CallAudioQualityKind.Medium)]
    [InlineData(CallAudioQualityKind.High)]
    public void RoundTrip_DomainToProtoToDomain_IsPreserved(CallAudioQualityKind quality)
        => quality.ToProto().ToDomain().Should().Be(quality);

    [Fact]
    public void ToDomain_UnknownValue_FallsBackToAuto()
        => ((ProtoQuality)999).ToDomain().Should().Be(CallAudioQualityKind.Auto);
}
