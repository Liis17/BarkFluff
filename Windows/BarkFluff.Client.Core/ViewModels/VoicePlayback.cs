using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;

namespace BarkFluff.Client.Core.ViewModels;

internal sealed class VoicePlayback(
    IVoicePlayer? player, IUiDispatcher dispatcher, ILocalizationService localization, TimeProvider clock)
{
    private CancellationTokenSource? _scope;
    private MessageAttachmentItemViewModel? _active;
    private bool _fromProfile;
    private IVoicePlayback? _playback;
    private ITimer? _timer;
    private TimeSpan _duration;

    public async Task ToggleAsync(MessageAttachmentItemViewModel item, bool fromProfile)
    {
        bool same = ReferenceEquals(_active, item);
        Stop();
        if (same || player is null || !item.IsVoice || string.IsNullOrWhiteSpace(item.FileUrl))
            return;

        var scope = new CancellationTokenSource();
        var token = scope.Token;
        _scope = scope;
        _active = item;
        _fromProfile = fromProfile;
        item.VoiceError = null;
        item.IsVoiceActive = true;
        try
        {
            var playback = await player.PlayAsync(item.FileUrl, item.Type == MessageAttachmentType.Voice,
                failed => dispatcher.Post(() => End(scope, failed)), token);
            if (_scope != scope)
            {
                playback.Dispose();
                return;
            }
            _playback = playback;
            _timer = clock.CreateTimer(_ => dispatcher.Post(() => Update(scope)), null,
                TimeSpan.Zero, TimeSpan.FromMilliseconds(200));
        }
        catch (Exception)
        {
            End(scope, true);
        }
    }

    private void Update(CancellationTokenSource scope)
    {
        if (_scope != scope || _active is not { } item || _playback is not { } playback)
            return;
        try
        {
            var duration = playback.Duration;
            if (duration <= TimeSpan.Zero)
                return;
            _duration = duration;
            var position = TimeSpan.FromTicks(Math.Clamp(playback.Position.Ticks, 0, duration.Ticks));
            item.VoiceProgress = position.TotalSeconds / duration.TotalSeconds * 100;
            item.VoiceTime = $"{Format(position)} / {Format(duration)}";
        }
        catch (Exception)
        {
            End(scope, true);
        }
    }

    private static string Format(TimeSpan time) => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";

    private void End(CancellationTokenSource scope, bool failed)
    {
        if (_scope != scope)
            return;
        if (failed && _active is { } item)
            item.VoiceError = localization.GetString("Messenger_VoicePlaybackFailed");
        Stop();
    }

    public void StopProfilePlayback()
    {
        if (_fromProfile)
            Stop();
    }

    public void Stop()
    {
        var scope = _scope;
        _scope = null;
        _timer?.Dispose();
        _timer = null;
        scope?.Cancel();
        scope?.Dispose();
        _playback?.Dispose();
        _playback = null;
        if (_active is { } item)
        {
            item.IsVoiceActive = false;
            item.VoiceProgress = 0;
            if (_duration > TimeSpan.Zero)
                item.VoiceTime = $"{Format(TimeSpan.Zero)} / {Format(_duration)}";
        }
        _active = null;
        _fromProfile = false;
        _duration = TimeSpan.Zero;
    }
}
