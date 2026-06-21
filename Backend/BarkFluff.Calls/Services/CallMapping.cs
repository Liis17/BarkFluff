using BarkFluff.Calls.Domain;

using ProtoEndReason = BarkFluff.Proto.Calls.CallEndReason;
using ProtoMediaType = BarkFluff.Proto.Calls.CallMediaType;

namespace BarkFluff.Calls.Services;

/// <summary>Маппинг доменных enum'ов звонка ↔ proto.</summary>
public static class CallMapping
{
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
