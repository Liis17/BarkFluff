using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.Infrastructure.Threading;
using BarkFluff.Client.Core.Services;
using BarkFluff.Proto.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarkFluff.Client.Core.ViewModels;

public sealed partial class VoicePlayback : ObservableObject
{
    private readonly IVoicePlayer? _player;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILocalizationService _localization;
    private readonly TimeProvider _clock;
    private readonly ISettingsPreferences? _preferences;

    private CancellationTokenSource? _scope;
    private MessageAttachmentItemViewModel? _active;
    private bool _playRequested;
    private IVoicePlayback? _playback;
    private ITimer? _timer;
    private TimeSpan _duration;
    private double? _preferredSpeed;
    private bool _isScrubbing;

    internal VoicePlayback(IVoicePlayer? player, IUiDispatcher dispatcher, ILocalizationService localization,
        TimeProvider clock, ISettingsPreferences? preferences)
    {
        _player = player;
        _dispatcher = dispatcher;
        _localization = localization;
        _clock = clock;
        _preferences = preferences;
    }

    internal (string ChatId, long MessageId)? Target { get; private set; }
    internal CancellationToken? Lifetime => _scope?.Token;
    public event Action? Starting;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _canSeek;
    [ObservableProperty] private bool _canChangeSpeed;
    [ObservableProperty] private bool _canJump;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanToggle))] private bool _callInProgress;
    public bool CanToggle => !CallInProgress;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Symbol), nameof(PlaybackAction))]
    private bool _isPlaying;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _time = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SpeedLabel))]
    private double _speed = 1;

    public string Symbol => IsPlaying ? "⏸" : "▶";
    public string PlaybackAction => _localization.GetString(IsPlaying ? "Messenger_VoicePause" : "Messenger_VoicePlay");
    public string SpeedLabel => $"{Speed:0.#}×";

    internal async Task ToggleAsync(MessageAttachmentItemViewModel item, (string ChatId, long MessageId)? target)
    {
        if (ReferenceEquals(_active, item) || (_active is not null && target is not null && Target == target &&
            _active.AttachmentId == item.AttachmentId && _active.FileId == item.FileId))
        {
            if (_playback is null)
                Stop();
            else
                Toggle();
            return;
        }

        if (_player is null || !item.IsVoice || string.IsNullOrWhiteSpace(item.FileUrl) || CallInProgress)
            return;

        Stop();
        var scope = new CancellationTokenSource();
        _scope = scope;
        _active = item;
        _playRequested = true;
        Target = target;
        CanJump = target is not null;
        item.VoiceError = null;
        item.IsVoiceActive = true;
        try
        {
            var isVoice = item.Type == MessageAttachmentType.Voice;
            if (isVoice && _preferredSpeed is null && _preferences is not null)
            {
                var savedSpeed = await _preferences.GetVoicePlaybackSpeedAsync(scope.Token);
                if (_scope != scope)
                    return;
                _preferredSpeed = savedSpeed;
            }

            Speed = isVoice ? _preferredSpeed ?? 1 : 1;
            var playback = await _player.PrepareAsync(item.FileUrl, isVoice,
                failed => _dispatcher.Post(() => End(scope, failed)), scope.Token);
            if (_scope != scope)
            {
                playback.Dispose();
                return;
            }
            _playback = playback;
            if (_playRequested)
                SetPlaying(true);
            else
                item.IsVoiceActive = false;
            CanChangeSpeed = isVoice;
            IsVisible = true;
            Update(scope);
            if (_scope == scope)
                _timer = _clock.CreateTimer(_ => _dispatcher.Post(() => Update(scope)), null,
                    TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
        }
        catch (Exception)
        {
            End(scope, true);
        }
    }

    [RelayCommand]
    private void Toggle()
    {
        if (!CallInProgress)
            ApplyPlaybackChange(() => SetPlaying(!IsPlaying));
    }

    private void SetPlaying(bool playing)
    {
        if (_playback is not { } playback || _active is not { } item || IsPlaying == playing)
            return;

        if (playing)
        {
            Starting?.Invoke();
            playback.Play(Speed);
        }
        else
        {
            playback.Pause();
        }

        IsPlaying = playing;
        item.IsVoiceActive = playing;
    }

    public void Pause()
    {
        _playRequested = false;
        ApplyPlaybackChange(() => SetPlaying(false));
    }

    internal void SetCallInProgress(bool active)
    {
        CallInProgress = active;
        if (active)
            Pause();
    }

    [RelayCommand]
    private async Task CycleSpeedAsync()
    {
        if (!CanChangeSpeed)
            return;

        var speed = Speed switch { 1 => 1.5, 1.5 => 2, _ => 1 };
        ApplyPlaybackChange(() =>
        {
            _playback!.SetSpeed(speed);
            Speed = speed;
        });
        if (!CanChangeSpeed)
            return;

        _preferredSpeed = speed;
        if (_preferences is not null)
            await _preferences.SaveVoicePlaybackSpeedAsync(speed);
    }

    public void BeginScrub() => _isScrubbing = CanSeek;

    public void EndScrub(double? percent)
    {
        if (!_isScrubbing)
            return;

        _isScrubbing = false;
        if (percent is { } value && value != Progress)
            Seek(value);
        else if (_scope is { } scope)
            Update(scope);
    }

    public void Seek(double percent)
    {
        if (!CanSeek || _isScrubbing || !double.IsFinite(percent))
            return;

        ApplyPlaybackChange(() =>
        {
            var position = TimeSpan.FromSeconds(_duration.TotalSeconds * Math.Clamp(percent, 0, 100) / 100);
            _playback!.Seek(position);
            SetPosition(position);
        });
    }

    private void ApplyPlaybackChange(Action change)
    {
        if (_scope is not { } scope || _playback is null)
            return;
        try
        {
            change();
        }
        catch (Exception)
        {
            End(scope, true);
        }
    }

    private void Update(CancellationTokenSource scope)
    {
        if (_scope != scope || _isScrubbing || _playback is not { } playback)
            return;
        ApplyPlaybackChange(() =>
        {
            var duration = playback.Duration;
            if (duration <= TimeSpan.Zero)
                return;
            _duration = duration;
            CanSeek = playback.CanSeek;
            SetPosition(TimeSpan.FromTicks(Math.Clamp(playback.Position.Ticks, 0, duration.Ticks)));
        });
    }

    private void SetPosition(TimeSpan position)
    {
        var item = _active!;
        item.VoiceProgress = position.TotalSeconds / _duration.TotalSeconds * 100;
        Progress = Math.Round(item.VoiceProgress);
        Time = item.VoiceTime = $"{Format(position)} / {Format(_duration)}";
    }

    private static string Format(TimeSpan time) => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";

    private void End(CancellationTokenSource scope, bool failed)
    {
        if (_scope != scope)
            return;
        if (failed && _active is { } item)
            item.VoiceError = _localization.GetString("Messenger_VoicePlaybackFailed");
        Stop();
    }

    internal void StopDeletedMessage(string chatId, long messageId)
    {
        if (Target == (chatId, messageId))
            Stop();
    }

    [RelayCommand]
    internal void Stop()
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
        _playRequested = false;
        _duration = TimeSpan.Zero;
        Target = null;
        IsVisible = false;
        IsPlaying = false;
        CanSeek = false;
        CanChangeSpeed = false;
        _isScrubbing = false;
        CanJump = false;
        Progress = 0;
        Time = string.Empty;
    }
}
