using BarkFluff.Proto.Calls;
using Grpc.Core;

namespace BarkFluff.Client.Core.Services.Calls;

public sealed partial class AudioCallService
{
    public void ToggleCamera()
    {
        if (_call is { Status.Phase: AudioCallPhase.Active } call)
            media.Send(call.Status.Id, call.Status.CameraOn ? "camera_off" : "camera_on");
    }

    public void SetVideoQuality(int quality)
    {
        if (_call is not { Status.Phase: AudioCallPhase.Active } call || quality is < 0 or > 3 ||
            quality == call.Status.VideoQuality)
            return;
        call.Status = call.Status with { VideoQuality = quality };
        media.Send(call.Status.Id, "video_quality", QualityValue(quality));
        Changed?.Invoke();
    }

    public async Task SetAudioQualityAsync(CallAudioQuality quality)
    {
        if (_session is not { } session || !IsCurrent(session) || session.AudioQualityUnsupported ||
            _call is not { Status.Phase: AudioCallPhase.Active, AudioQualityPending: false } call ||
            !Enum.IsDefined(quality) || call.Status.AudioQuality == quality)
            return;

        call.AudioQualityPending = true;
        Outcome = null;
        var revision = call.AudioQualityRevision;
        try
        {
            var response = await session.Signaling.SetAudioQualityAsync(call.Status.Id.ToString("D"), quality,
                session.Lifetime.Token);
            if (response is null)
                throw new InvalidOperationException();
            if (IsCurrent(session, call) && revision == call.AudioQualityRevision)
                ApplyAudioQuality(call, quality);
        }
        catch (Exception ex)
        {
            if (IsCurrent(session, call))
            {
                session.AudioQualityUnsupported = ex is RpcException { StatusCode: StatusCode.Unimplemented };
                Outcome = "Calls_QualityError";
            }
        }
        finally
        {
            call.AudioQualityPending = false;
            if (IsCurrent(session, call))
                Changed?.Invoke();
        }
    }

    private void ApplyAudioQuality(Call call, CallAudioQuality quality)
    {
        if (call.Status.AudioQuality != quality)
        {
            call.Status = call.Status with { AudioQuality = quality };
            SyncAudioQuality(call);
            Changed?.Invoke();
        }
    }

    private void SyncAudioQuality(Call call)
    {
        if (call.Connected && call.MediaAudioQuality != call.Status.AudioQuality)
        {
            call.MediaAudioQuality = call.Status.AudioQuality;
            media.Send(call.Status.Id, "audio_quality", QualityValue((int)call.MediaAudioQuality));
        }
    }

    private static string QualityValue(int quality) =>
        quality.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
