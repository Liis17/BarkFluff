using BarkFluff.Client.Core.Services;
using BarkFluff.Client.Core.ViewModels;
using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace BarkFluff.Client.WinUI.Infrastructure;

internal sealed class WindowsVoiceRecorder : IVoiceRecorder
{
    public async Task<IVoiceRecording> StartAsync(Action interrupted, CancellationToken cancellationToken)
    {
        var recording = new Recording(interrupted);
        try
        {
            await recording.StartAsync(cancellationToken);
            return recording;
        }
        catch
        {
            recording.Dispose();
            throw;
        }
    }

    private sealed class Recording : IVoiceRecording
    {
        private MediaCapture? _capture;
        private readonly InMemoryRandomAccessStream _wave = new();
        private readonly Action _interrupted;
        private DeviceWatcher? _devices;
        private string? _deviceId;
        private bool _released;

        public Recording(Action interrupted) => _interrupted = interrupted;

        public async Task StartAsync(CancellationToken token)
        {
            _deviceId = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default);
            if (string.IsNullOrEmpty(_deviceId))
                throw new InvalidOperationException();

            _devices = DeviceInformation.CreateWatcher(MediaDevice.GetAudioCaptureSelector(),
                ["System.Devices.InterfaceEnabled"]);
            _devices.Added += OnDeviceAdded;
            _devices.Removed += OnDeviceRemoved;
            _devices.Updated += OnDeviceUpdated;
            _devices.Start();

            MediaCapture capture;
            try
            {
                capture = await InitializeCaptureAsync(AudioProcessing.Raw, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !token.IsCancellationRequested)
            {
                capture = await InitializeCaptureAsync(AudioProcessing.Default, token);
            }
            _capture = capture;
            capture.Failed += OnFailed;
            capture.RecordLimitationExceeded += OnLimit;
            var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.Auto);
            profile.Audio = AudioEncodingProperties.CreatePcm(VoiceOggCodec.SampleRate, 1, 16);
            await capture.StartRecordToStreamAsync(profile, _wave).AsTask(token);
        }

        private async Task<MediaCapture> InitializeCaptureAsync(AudioProcessing processing, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var capture = new MediaCapture();
            try
            {
                await capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    AudioDeviceId = _deviceId,
                    StreamingCaptureMode = StreamingCaptureMode.Audio,
                    MediaCategory = MediaCategory.Other,
                    AudioProcessing = processing
                }).AsTask(token);
                return capture;
            }
            catch
            {
                capture.Dispose();
                throw;
            }
        }

        public async Task<Stream> StopAsync(CancellationToken cancellationToken)
        {
            try { await _capture!.StopRecordAsync().AsTask(cancellationToken); }
            finally { ReleaseDevice(); }
            _wave.Seek(0);
            return _wave.AsStreamForRead();
        }

        private void OnFailed(MediaCapture sender, MediaCaptureFailedEventArgs args) => _interrupted();
        private void OnLimit(MediaCapture sender) => _interrupted();

        private static void OnDeviceAdded(DeviceWatcher sender, DeviceInformation device) { }

        private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate device)
        {
            if (device.Id == _deviceId)
                _interrupted();
        }

        private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate device)
        {
            if (device.Id == _deviceId &&
                device.Properties.TryGetValue("System.Devices.InterfaceEnabled", out var enabled) && enabled is false)
                _interrupted();
        }

        private void ReleaseDevice()
        {
            if (_released)
                return;
            _released = true;
            if (_capture is { } capture)
            {
                capture.Failed -= OnFailed;
                capture.RecordLimitationExceeded -= OnLimit;
            }
            try
            {
                if (_devices is { } watcher)
                {
                    watcher.Added -= OnDeviceAdded;
                    watcher.Removed -= OnDeviceRemoved;
                    watcher.Updated -= OnDeviceUpdated;
                    if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                        watcher.Stop();
                }
            }
            finally { _capture?.Dispose(); }
        }

        public void Dispose()
        {
            try { ReleaseDevice(); }
            finally { _wave.Dispose(); }
        }
    }
}

internal sealed class WindowsVoicePlayer : IVoicePlayer, IDisposable
{
    private readonly HttpClient _http = new();

    public void Dispose() => _http.Dispose();

    public async Task<IVoicePlayback> PrepareAsync(string url, bool isOpus, Action<bool> ended, CancellationToken cancellationToken)
    {
        var playback = new Playback(ended);
        try
        {
            var uri = new Uri(url);
            if (uri.Scheme is not ("http" or "https"))
                throw new InvalidDataException();
            if (isOpus)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                var token = timeout.Token;
                using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                using var ogg = TemporaryStream(".ogg");
                await using var content = await response.Content.ReadAsStreamAsync(token);
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, token)) != 0)
                {
                    if (ogg.Length + read > 32 * 1024 * 1024)
                        throw new InvalidDataException();
                    await ogg.WriteAsync(buffer.AsMemory(0, read), token);
                }
                ogg.Position = 0;
                var wave = TemporaryStream(".wav");
                try
                {
                    await Task.Run(() => VoiceOggCodec.DecodeWave(ogg, wave, token), token);
                    wave.Position = 0;
                    playback.Wave = wave.AsRandomAccessStream();
                }
                catch
                {
                    wave.Dispose();
                    throw;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            playback.SetSource(uri);
            return playback;
        }
        catch
        {
            playback.Dispose();
            throw;
        }
    }

    private static FileStream TemporaryStream(string extension)
    {
        Directory.CreateDirectory(VoiceComposer.TemporaryDirectory);
        return new(Path.Combine(VoiceComposer.TemporaryDirectory, $"barkfluff-{Guid.NewGuid():N}{extension}"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
    }

    private sealed class Playback : IVoicePlayback
    {
        private readonly MediaPlayer _player = new() { AutoPlay = false };
        private readonly Action<bool> _ended;
        private MediaSource? _source;
        public IRandomAccessStream? Wave { get; set; }
        public TimeSpan Position => _player.PlaybackSession.Position;
        public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;
        public bool CanSeek => _player.PlaybackSession.CanSeek;

        public void Play(double speed)
        {
            SetSpeed(speed);
            _player.Play();
        }

        public void Pause() => _player.Pause();
        public void Seek(TimeSpan position) => _player.PlaybackSession.Position = position;
        public void SetSpeed(double speed) => _player.PlaybackSession.PlaybackRate = speed;

        public Playback(Action<bool> ended)
        {
            _ended = ended;
            _player.CommandManager.IsEnabled = false;
            _player.MediaEnded += OnEnded;
            _player.MediaFailed += OnFailed;
        }

        public void SetSource(Uri uri)
        {
            _source = Wave is { } wave ? MediaSource.CreateFromStream(wave, "audio/wav") : MediaSource.CreateFromUri(uri);
            _player.Source = _source;
        }

        private void OnEnded(MediaPlayer sender, object args) => _ended(false);
        private void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => _ended(true);

        public void Dispose()
        {
            _player.MediaEnded -= OnEnded;
            _player.MediaFailed -= OnFailed;
            _player.Dispose();
            _source?.Dispose();
            Wave?.Dispose();
        }
    }
}
