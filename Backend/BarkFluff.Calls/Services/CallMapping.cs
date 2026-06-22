using BarkFluff.Calls.Domain;

using ProtoAudioQuality = BarkFluff.Proto.Calls.CallAudioQuality;
using ProtoEndReason = BarkFluff.Proto.Calls.CallEndReason;
using ProtoMediaType = BarkFluff.Proto.Calls.CallMediaType;

namespace BarkFluff.Calls.Services;

/// <summary>Маппинг доменных enum'ов звонка ↔ proto.</summary>
public static class CallMapping
{
    public static CallAudioQualityKind ToDomain(this ProtoAudioQuality quality) => quality switch
    {
        ProtoAudioQuality.Low => CallAudioQualityKind.Low,
        ProtoAudioQuality.Medium => CallAudioQualityKind.Medium,
        ProtoAudioQuality.High => CallAudioQualityKind.High,
        _ => CallAudioQualityKind.Auto,
    };

    public static ProtoAudioQuality ToProto(this CallAudioQualityKind quality) => quality switch
    {
        CallAudioQualityKind.Low => ProtoAudioQuality.Low,
        CallAudioQualityKind.Medium => ProtoAudioQuality.Medium,
        CallAudioQualityKind.High => ProtoAudioQuality.High,
        _ => ProtoAudioQuality.Auto,
    };

    public static CallMediaKind ToDomain(this ProtoMediaType media) => media switch
    {
        ProtoMediaType.CallMediaAudio => CallMediaKind.Audio,
        ProtoMediaType.CallMediaVideo => CallMediaKind.Video,
        _ => CallMediaKind.Unknown,
    };

    public static ProtoMediaType ToProto(this CallMediaKind media) => media switch
    {
        CallMediaKind.Audio => ProtoMediaType.CallMediaAudio,
        CallMediaKind.Video => ProtoMediaType.CallMediaVideo,
        _ => ProtoMediaType.Unknown,
    };

    public static ProtoEndReason ToProto(this CallEndReasonKind reason) => reason switch
    {
        CallEndReasonKind.Hangup => ProtoEndReason.CallEndHangup,
        CallEndReasonKind.Rejected => ProtoEndReason.CallEndRejected,
        CallEndReasonKind.Missed => ProtoEndReason.CallEndMissed,
        CallEndReasonKind.Busy => ProtoEndReason.CallEndBusy,
        CallEndReasonKind.Failed => ProtoEndReason.CallEndFailed,
        _ => ProtoEndReason.Unknown,
    };
}
